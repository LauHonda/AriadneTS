#ifndef TSRUNTIME_TS_EXPORT_H
#define TSRUNTIME_TS_EXPORT_H

#if defined(_WIN32)
#  if defined(TSRUNTIME_BUILD_SHARED)
#    define TSRUNTIME_API __declspec(dllexport)
#  elif defined(TSRUNTIME_USE_SHARED)
#    define TSRUNTIME_API __declspec(dllimport)
#  else
#    define TSRUNTIME_API
#  endif
#else
#  define TSRUNTIME_API __attribute__((visibility("default")))
#endif

#endif

