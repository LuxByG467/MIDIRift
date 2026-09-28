
#include <aaudio/AAudio.h>
#include <android/log.h>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <thread>
#include <vector>

#define LOG_TAG "MIDIRiftRT"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

namespace {

constexpr int kChannels = 2;
constexpr int kInitialBursts = 3;
constexpr int kProbeMs = 500;
constexpr int kIncreaseCooldownMs = 750;

struct RtAudioState {
    int sampleRate = 44100;
    int channels = kChannels;
    int requestedBufferFrames = 4096;

    AAudioStream* stream = nullptr;

    std::vector<float> fifo;
    uint64_t fifoMask = 0;
    std::atomic<uint64_t> writeIndex{0};
    std::atomic<uint64_t> readIndex{0};

    // Inicio del epoch de presentación actual, expresado en samples
    // interleaved del FIFO. El callback avanza readIndex únicamente por PCM
    // real consumido; los ceros por starvation NO cuentan como canción
    // presentada.
    std::atomic<uint64_t> presentationBaseReadIndex{0};

    std::atomic<int64_t> starvationCount{0};
    std::atomic<bool> acceptWrites{true};

    std::atomic<int32_t> framesPerBurst{0};
    std::atomic<int32_t> bufferCapacityFrames{0};
    std::atomic<int32_t> adaptiveBufferFrames{0};
    std::atomic<int32_t> adaptiveIncreaseCount{0};

    int32_t lastObservedXruns = 0;
    int64_t lastObservedStarvations = 0;
    std::chrono::steady_clock::time_point lastProbe{};
    std::chrono::steady_clock::time_point lastIncrease{};

    std::atomic<bool> disposed{false};
};

static size_t nextPowerOfTwo(size_t value) {
    size_t result = 1;
    while (result < value) result <<= 1;
    return result;
}

static aaudio_data_callback_result_t dataCallback(
    AAudioStream*,
    void* userData,
    void* audioData,
    int32_t numFrames) {

    auto* state = static_cast<RtAudioState*>(userData);
    if (!state || !audioData || numFrames <= 0) {
        return AAUDIO_CALLBACK_RESULT_CONTINUE;
    }

    const uint64_t requestedSamples =
        static_cast<uint64_t>(numFrames) * static_cast<uint64_t>(state->channels);

    const uint64_t read = state->readIndex.load(std::memory_order_relaxed);
    const uint64_t write = state->writeIndex.load(std::memory_order_acquire);
    const uint64_t availableTotal = write >= read ? write - read : 0;
    uint64_t available = availableTotal < requestedSamples ? availableTotal : requestedSamples;
    available -= available % static_cast<uint64_t>(state->channels);

    auto* out = static_cast<float*>(audioData);

    if (available > 0) {
        const size_t pos = static_cast<size_t>(read & state->fifoMask);
        const size_t first =
            static_cast<size_t>(available) < state->fifo.size() - pos
                ? static_cast<size_t>(available)
                : state->fifo.size() - pos;

        std::memcpy(out, state->fifo.data() + pos, first * sizeof(float));

        if (first < available) {
            std::memcpy(
                out + first,
                state->fifo.data(),
                (static_cast<size_t>(available) - first) * sizeof(float));
        }

        state->readIndex.store(read + available, std::memory_order_release);
    }

    if (available < requestedSamples) {
        std::memset(
            out + available,
            0,
            static_cast<size_t>(requestedSamples - available) * sizeof(float));
        state->starvationCount.fetch_add(1, std::memory_order_relaxed);
    }

    return AAUDIO_CALLBACK_RESULT_CONTINUE;
}

static void closeStream(RtAudioState* state) {
    if (!state || !state->stream) return;

    AAudioStream_requestStop(state->stream);
    AAudioStream_close(state->stream);
    state->stream = nullptr;
}

static int openStream(RtAudioState* state) {
    if (!state) return AAUDIO_ERROR_NULL;
    if (state->stream) return AAUDIO_OK;

    AAudioStreamBuilder* builder = nullptr;
    aaudio_result_t result = AAudio_createStreamBuilder(&builder);
    if (result != AAUDIO_OK || !builder) {
        return result == AAUDIO_OK ? AAUDIO_ERROR_INTERNAL : result;
    }

    AAudioStreamBuilder_setDirection(builder, AAUDIO_DIRECTION_OUTPUT);
    AAudioStreamBuilder_setSampleRate(builder, state->sampleRate);
    AAudioStreamBuilder_setChannelCount(builder, state->channels);
    AAudioStreamBuilder_setFormat(builder, AAUDIO_FORMAT_PCM_FLOAT);
    AAudioStreamBuilder_setSharingMode(builder, AAUDIO_SHARING_MODE_SHARED);
    AAudioStreamBuilder_setPerformanceMode(builder, AAUDIO_PERFORMANCE_MODE_LOW_LATENCY);

#if __ANDROID_API__ >= 28
    AAudioStreamBuilder_setUsage(builder, AAUDIO_USAGE_MEDIA);
    AAudioStreamBuilder_setContentType(builder, AAUDIO_CONTENT_TYPE_MUSIC);
#endif

    AAudioStreamBuilder_setBufferCapacityInFrames(
        builder,
        state->requestedBufferFrames);

    // Critical difference from RT-1: callback terminates entirely in native code.
    AAudioStreamBuilder_setDataCallback(builder, dataCallback, state);

    result = AAudioStreamBuilder_openStream(builder, &state->stream);
    AAudioStreamBuilder_delete(builder);

    if (result != AAUDIO_OK || !state->stream) {
        state->stream = nullptr;
        return result == AAUDIO_OK ? AAUDIO_ERROR_INTERNAL : result;
    }

    const int32_t capacity =
        std::max<int32_t>(1, AAudioStream_getBufferCapacityInFrames(state->stream));
    const int32_t burst =
        std::max<int32_t>(1, AAudioStream_getFramesPerBurst(state->stream));

    const int32_t ceiling =
        std::min(std::max(state->requestedBufferFrames, burst), capacity);
    const int32_t initialTarget =
        std::min(ceiling, std::max(burst, kInitialBursts * burst));

    int32_t actual = AAudioStream_setBufferSizeInFrames(state->stream, initialTarget);
    if (actual <= 0) {
        actual = std::max<int32_t>(1, AAudioStream_getBufferSizeInFrames(state->stream));
    }

    state->framesPerBurst.store(burst, std::memory_order_relaxed);
    state->bufferCapacityFrames.store(capacity, std::memory_order_relaxed);
    state->adaptiveBufferFrames.store(actual, std::memory_order_relaxed);
    state->adaptiveIncreaseCount.store(0, std::memory_order_relaxed);
    state->lastObservedXruns =
        std::max<int32_t>(0, AAudioStream_getXRunCount(state->stream));
    state->lastObservedStarvations =
        state->starvationCount.load(std::memory_order_relaxed);
    state->lastProbe = std::chrono::steady_clock::now();
    state->lastIncrease = {};

    LOGI(
        "open rate=%d channels=%d burst=%d capacity=%d buffer=%d fifoFrames=%zu",
        AAudioStream_getSampleRate(state->stream),
        AAudioStream_getChannelCount(state->stream),
        burst,
        capacity,
        actual,
        state->fifo.size() / static_cast<size_t>(state->channels));

    return AAUDIO_OK;
}

static void clearFifo(RtAudioState* state) {
    if (!state) return;
    const uint64_t write = state->writeIndex.load(std::memory_order_acquire);
    state->readIndex.store(write, std::memory_order_release);
    state->presentationBaseReadIndex.store(write, std::memory_order_release);
}

static void maybeAdaptBuffer(RtAudioState* state) {
    if (!state || !state->stream) return;

    const auto now = std::chrono::steady_clock::now();
    if (state->lastProbe.time_since_epoch().count() != 0) {
        const auto elapsed =
            std::chrono::duration_cast<std::chrono::milliseconds>(now - state->lastProbe).count();
        if (elapsed < kProbeMs) return;
    }
    state->lastProbe = now;

    const int32_t xruns =
        std::max<int32_t>(0, AAudioStream_getXRunCount(state->stream));
    const int64_t starvations =
        state->starvationCount.load(std::memory_order_relaxed);

    const bool newUnderrun =
        xruns > state->lastObservedXruns ||
        starvations > state->lastObservedStarvations;

    state->lastObservedXruns = xruns;
    state->lastObservedStarvations = starvations;

    if (!newUnderrun) return;

    if (state->lastIncrease.time_since_epoch().count() != 0) {
        const auto elapsed =
            std::chrono::duration_cast<std::chrono::milliseconds>(now - state->lastIncrease).count();
        if (elapsed < kIncreaseCooldownMs) return;
    }

    const int32_t burst = std::max<int32_t>(
        1, state->framesPerBurst.load(std::memory_order_relaxed));
    const int32_t capacity = std::max<int32_t>(
        burst, state->bufferCapacityFrames.load(std::memory_order_relaxed));
    const int32_t current = std::max<int32_t>(
        burst, AAudioStream_getBufferSizeInFrames(state->stream));

    if (current >= capacity) return;

    const int32_t target = std::min(capacity, current + burst);
    int32_t actual = AAudioStream_setBufferSizeInFrames(state->stream, target);
    if (actual <= 0) {
        actual = AAudioStream_getBufferSizeInFrames(state->stream);
    }

    if (actual > current) {
        state->adaptiveBufferFrames.store(actual, std::memory_order_relaxed);
        state->adaptiveIncreaseCount.fetch_add(1, std::memory_order_relaxed);
        state->lastIncrease = now;
        LOGI(
            "adaptive buffer %d->%d frames burst=%d xruns=%d starvation=%lld",
            current,
            actual,
            burst,
            xruns,
            static_cast<long long>(starvations));
    }
}

} // namespace

extern "C" {

__attribute__((visibility("default")))
void* midirift_rt_create(
    int sampleRate,
    int channelCount,
    int requestedBufferFrames) {

    if (sampleRate <= 0 || channelCount != kChannels || requestedBufferFrames <= 0) {
        return nullptr;
    }

    auto state = std::make_unique<RtAudioState>();
    state->sampleRate = sampleRate;
    state->channels = channelCount;
    state->requestedBufferFrames = requestedBufferFrames;

    const size_t requestedSamples = std::max<size_t>(
        static_cast<size_t>(requestedBufferFrames) *
            static_cast<size_t>(channelCount) * 4u,
        static_cast<size_t>(sampleRate) *
            static_cast<size_t>(channelCount) / 6u);

    const size_t fifoSize = nextPowerOfTwo(requestedSamples);
    try {
        state->fifo.resize(fifoSize, 0.0f);
    } catch (...) {
        return nullptr;
    }
    state->fifoMask = static_cast<uint64_t>(fifoSize - 1u);

    const int result = openStream(state.get());
    if (result != AAUDIO_OK) {
        LOGE("midirift_rt_create open failed: %d (%s)", result, AAudio_convertResultToText(result));
        return nullptr;
    }

    return state.release();
}

__attribute__((visibility("default")))
int midirift_rt_start(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || state->disposed.load(std::memory_order_acquire)) {
        return AAUDIO_ERROR_INVALID_STATE;
    }

    if (!state->stream) {
        const int open = openStream(state);
        if (open != AAUDIO_OK) return open;
    }

    state->acceptWrites.store(true, std::memory_order_release);

    const auto current = AAudioStream_getState(state->stream);
    if (current == AAUDIO_STREAM_STATE_STARTED ||
        current == AAUDIO_STREAM_STATE_STARTING) {
        return AAUDIO_OK;
    }

    return AAudioStream_requestStart(state->stream);
}

__attribute__((visibility("default")))
int midirift_rt_pause(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || !state->stream) return AAUDIO_ERROR_INVALID_STATE;

    const auto current = AAudioStream_getState(state->stream);
    if (current == AAUDIO_STREAM_STATE_PAUSED ||
        current == AAUDIO_STREAM_STATE_PAUSING ||
        current == AAUDIO_STREAM_STATE_OPEN) {
        return AAUDIO_OK;
    }

    return AAudioStream_requestPause(state->stream);
}

__attribute__((visibility("default")))
int midirift_rt_flush(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || !state->stream) return AAUDIO_ERROR_INVALID_STATE;

    clearFifo(state);

    const auto current = AAudioStream_getState(state->stream);
    if (current == AAUDIO_STREAM_STATE_STARTED ||
        current == AAUDIO_STREAM_STATE_STARTING) {
        AAudioStream_requestPause(state->stream);
        // Flush is intentionally best-effort here. Seek uses recreate below.
        return AAUDIO_OK;
    }

    if (current == AAUDIO_STREAM_STATE_PAUSED ||
        current == AAUDIO_STREAM_STATE_OPEN ||
        current == AAUDIO_STREAM_STATE_STOPPED ||
        current == AAUDIO_STREAM_STATE_FLUSHED) {
        const auto result = AAudioStream_requestFlush(state->stream);
        return result == AAUDIO_ERROR_INVALID_STATE ? AAUDIO_OK : result;
    }

    return AAUDIO_OK;
}

__attribute__((visibility("default")))
int midirift_rt_reset_for_seek(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || state->disposed.load(std::memory_order_acquire)) {
        return AAUDIO_ERROR_INVALID_STATE;
    }

    state->acceptWrites.store(false, std::memory_order_release);
    clearFifo(state);
    closeStream(state);

    const int result = openStream(state);
    state->acceptWrites.store(result == AAUDIO_OK, std::memory_order_release);
    return result;
}

__attribute__((visibility("default")))
int midirift_rt_stop(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state) return AAUDIO_ERROR_INVALID_STATE;

    state->acceptWrites.store(false, std::memory_order_release);
    clearFifo(state);

    if (!state->stream) return AAUDIO_OK;

    const auto current = AAudioStream_getState(state->stream);
    if (current == AAUDIO_STREAM_STATE_STOPPED ||
        current == AAUDIO_STREAM_STATE_STOPPING ||
        current == AAUDIO_STREAM_STATE_CLOSED) {
        return AAUDIO_OK;
    }

    const auto result = AAudioStream_requestStop(state->stream);
    return result == AAUDIO_ERROR_INVALID_STATE ? AAUDIO_OK : result;
}

__attribute__((visibility("default")))
int midirift_rt_write(void* handle, const float* samples, int sampleCount) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || !samples || sampleCount <= 0) return 0;
    if (state->disposed.load(std::memory_order_acquire) ||
        !state->acceptWrites.load(std::memory_order_acquire)) {
        return 0;
    }

    int bounded = sampleCount - (sampleCount % state->channels);
    int copied = 0;

    while (copied < bounded) {
        if (state->disposed.load(std::memory_order_acquire) ||
            !state->acceptWrites.load(std::memory_order_acquire)) {
            break;
        }

        const uint64_t write = state->writeIndex.load(std::memory_order_relaxed);
        const uint64_t read = state->readIndex.load(std::memory_order_acquire);
        const uint64_t used = write >= read ? write - read : 0;
        const size_t freeSamples =
            used >= state->fifo.size()
                ? 0
                : state->fifo.size() - static_cast<size_t>(used);

        if (freeSamples < static_cast<size_t>(state->channels)) {
            std::this_thread::yield();
            continue;
        }

        size_t count = std::min<size_t>(
            static_cast<size_t>(bounded - copied),
            freeSamples);
        count -= count % static_cast<size_t>(state->channels);
        if (count == 0) continue;

        const size_t pos = static_cast<size_t>(write & state->fifoMask);
        const size_t first = std::min(count, state->fifo.size() - pos);

        std::memcpy(
            state->fifo.data() + pos,
            samples + copied,
            first * sizeof(float));

        if (first < count) {
            std::memcpy(
                state->fifo.data(),
                samples + copied + first,
                (count - first) * sizeof(float));
        }

        state->writeIndex.store(write + count, std::memory_order_release);
        copied += static_cast<int>(count);
    }

    maybeAdaptBuffer(state);
    return copied;
}

__attribute__((visibility("default")))
int midirift_rt_get_xruns(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state) return 0;

    int native = 0;
    if (state->stream) {
        native = std::max<int32_t>(0, AAudioStream_getXRunCount(state->stream));
    }

    const int64_t starvation =
        state->starvationCount.load(std::memory_order_relaxed);
    const int64_t total = static_cast<int64_t>(native) + starvation;
    return total > INT32_MAX ? INT32_MAX : static_cast<int>(total);
}

__attribute__((visibility("default")))
int midirift_rt_get_buffered_frames(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state) return 0;

    const uint64_t write = state->writeIndex.load(std::memory_order_acquire);
    const uint64_t read = state->readIndex.load(std::memory_order_acquire);
    const uint64_t samples = write >= read ? write - read : 0;
    const uint64_t frames = samples / static_cast<uint64_t>(state->channels);
    return frames > INT32_MAX ? INT32_MAX : static_cast<int>(frames);
}

__attribute__((visibility("default")))
int64_t midirift_rt_get_presented_frames(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state) return 0;

    const uint64_t read =
        state->readIndex.load(std::memory_order_acquire);
    const uint64_t base =
        state->presentationBaseReadIndex.load(std::memory_order_acquire);
    const uint64_t samples = read >= base ? read - base : 0;
    const uint64_t frames =
        samples / static_cast<uint64_t>(state->channels);

    return frames > static_cast<uint64_t>(INT64_MAX)
        ? INT64_MAX
        : static_cast<int64_t>(frames);
}

__attribute__((visibility("default")))
int midirift_rt_get_frames_per_burst(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    return state ? state->framesPerBurst.load(std::memory_order_relaxed) : 0;
}

__attribute__((visibility("default")))
int midirift_rt_get_buffer_frames(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    return state ? state->adaptiveBufferFrames.load(std::memory_order_relaxed) : 0;
}

__attribute__((visibility("default")))
int midirift_rt_get_adaptive_increase_count(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    return state ? state->adaptiveIncreaseCount.load(std::memory_order_relaxed) : 0;
}

__attribute__((visibility("default")))
int midirift_rt_get_sample_rate(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || !state->stream) return 0;
    return AAudioStream_getSampleRate(state->stream);
}

__attribute__((visibility("default")))
int midirift_rt_get_channel_count(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state || !state->stream) return 0;
    return AAudioStream_getChannelCount(state->stream);
}

__attribute__((visibility("default")))
void midirift_rt_destroy(void* handle) {
    auto* state = static_cast<RtAudioState*>(handle);
    if (!state) return;

    state->disposed.store(true, std::memory_order_release);
    state->acceptWrites.store(false, std::memory_order_release);
    clearFifo(state);
    closeStream(state);
    delete state;
}

} // extern "C"
