/* YM3438 rendering bridge for SegaSonic FL Studio SoundFont creation.
 * Requires runtime/src/ym3438.c and runtime/include/s32/ym3438.h
 * from https://github.com/cheviman/segasonic-decomp (BSD 3-Clause).
 * This bridge contains no Sega ROM data.
 */
#include <stdint.h>
#include <string.h>
#include "s32/ym3438.h"
#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif
static void wr(s32_fm *chip, int reg, int val) {
    s32_fm_write(chip, 0, (uint8_t)reg);
    s32_fm_write(chip, 1, (uint8_t)val);
}
/* patch[0..19]: five registers per op in register order 1,3,2,4;
 * patch[20]: ALG/FB; patch[21..24]: TL in same register order.
 * note is the actual 0..127 Sega sound-driver note code index (after template transpose).
 * output is mono signed 16-bit PCM at approx 55,930 Hz (32.2159MHz/4/144).
 */
EXPORT int ss_render(const uint8_t *patch, int note, int length, int16_t *out, int capacity) {
    static const uint16_t fnums[12] = {
        0x269,0x28E,0x2B5,0x2DE,0x30A,0x338,
        0x369,0x39D,0x3D4,0x40E,0x44C,0x48D
    };
    static const int offsets[4]={0,8,4,12};
    s32_fm chip={0};
    int fnum,block,k,i;
    if(!patch || !out || note<0 || note>127 || length<0 || capacity<length) return -1;
    s32_fm_init(&chip);
    /* The driver's FM operator initialization clears SSG-EG, and its B4 is C0 (both outputs). */
    for(k=0;k<4;k++) {
        int off=offsets[k],source_index=off/4;
        wr(&chip,0x30+off,patch[0+source_index]);
        wr(&chip,0x40+off,patch[21+source_index]);
        wr(&chip,0x50+off,patch[4+source_index]);
        wr(&chip,0x60+off,patch[8+source_index]);
        wr(&chip,0x70+off,patch[12+source_index]);
        wr(&chip,0x80+off,patch[16+source_index]);
        wr(&chip,0x90+off,0);
    }
    wr(&chip,0xB0,patch[20]);
    wr(&chip,0xB4,0xC0);
    fnum=fnums[note%12];
    block=(note/12)&7;
    wr(&chip,0xA4,((block&7)<<3)|((fnum>>8)&7));
    wr(&chip,0xA0,fnum&0xFF);
    wr(&chip,0x28,0xF0);
    for(i=0;i<length;i++) {
        int32_t lr[2];
        int32_t x;
        s32_fm_generate(&chip,lr);
        x=(lr[0]+lr[1])/2;
        if(x>32767) x=32767;
        if(x< -32768) x=-32768;
        out[i]=(int16_t)x;
    }
    return length;
}
