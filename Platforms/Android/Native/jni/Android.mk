LOCAL_PATH := $(call my-dir)

include $(CLEAR_VARS)
LOCAL_MODULE := midirift_rt_audio
LOCAL_SRC_FILES := midirift_rt_audio.cpp
LOCAL_CPPFLAGS := -std=c++17 -O3 -fvisibility=hidden -fexceptions
LOCAL_LDLIBS := -laaudio -llog
include $(BUILD_SHARED_LIBRARY)
