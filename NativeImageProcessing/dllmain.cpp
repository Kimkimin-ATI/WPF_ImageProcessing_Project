// dllmain.cpp : DLL 애플리케이션의 진입점을 정의합니다.
#include "pch.h"
#include <emmintrin.h>

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}



extern "C" __declspec(dllexport) int Add(int a, int b)
{
    return a + b;
}

extern "C" __declspec(dllexport) long long CalculateSSD_SSE2CPP(unsigned char* originalGray, unsigned char* templateGray, int originalStartIndex, int templateStartIndex, int length)
{
    long long score = 0;
	__m128i zero = _mm_setzero_si128();
    int i = 0;
    for (; i <= length - 16; i += 16) {
        __m128i originalVector = _mm_loadu_si128((__m128i*)(originalGray + originalStartIndex + i));
        __m128i templateVector = _mm_loadu_si128((__m128i*)(templateGray + templateStartIndex + i));

        __m128i originallow = _mm_unpacklo_epi8(originalVector, zero);
        __m128i originalhigh = _mm_unpackhi_epi8(originalVector, zero);

        __m128i templatelow = _mm_unpacklo_epi8(templateVector, zero);
        __m128i templatehigh = _mm_unpackhi_epi8(templateVector, zero);

        __m128i diffLow = _mm_sub_epi16(originallow, templatelow);
        __m128i diffHigh = _mm_sub_epi16(originalhigh, templatehigh);

        __m128i squareLow = _mm_mullo_epi16(diffLow, diffLow);
        __m128i squareHigh = _mm_mullo_epi16(diffHigh, diffHigh);

        unsigned short lowValues[8];
        unsigned short highValues[8];

        _mm_storeu_si128((__m128i*)lowValues, squareLow);
        _mm_storeu_si128((__m128i*)highValues, squareHigh);

        for (int j = 0; j < 8; j++) {
            score += lowValues[j];
            score += highValues[j];
        }
    }
        for (; i <  length; i++) {
			int diff = originalGray[originalStartIndex + i] - templateGray[templateStartIndex + i];
			score += diff * diff;
        }


        /*
        for (int i = 0; i < length; i++) {
            int diff = originalGray[originalStartIndex + i] - templateGray[templateStartIndex + i];
            score += diff * diff;
        }
        */
    
    return score;

}