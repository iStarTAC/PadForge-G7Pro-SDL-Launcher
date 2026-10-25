#pragma once
#include <array>
#include <cstdint>
#include <cstring>
inline std::array<uint8_t,65> telemetryFrame(uint8_t const* payload, uint8_t batteryStatus,
                                             bool batteryValid, uint8_t sequence) {
    std::array<uint8_t,65> frame{};
    frame[0] = ((payload[53] & 0x08) ? 1 : 0) | ((payload[53] & 0x10) ? 2 : 0) |
        ((payload[54] & 0x01) ? 4 : 0) | ((payload[54] & 0x02) ? 8 : 0) |
        (batteryValid ? 0x80 : 0);
    frame[1] = 0x10;
    frame[2] = batteryStatus;
    frame[3] = sequence;
    frame[4] = 60;
    std::memcpy(frame.data()+5,payload,60);
    return frame;
}
inline bool telemetryFrameSelfTest() {
    std::array<uint8_t,60> payload{};
    for (unsigned i=0;i<payload.size();++i) payload[i]=static_cast<uint8_t>(i*31);
    payload[0]=0xe0;
    for (unsigned extras=0;extras<16;++extras) {
        payload[53]=static_cast<uint8_t>(((extras&1)?8:0)|((extras&2)?16:0));
        payload[54]=static_cast<uint8_t>((extras>>2)&3);
        auto frame=telemetryFrame(payload.data(),0x87,true,197);
        if (frame[0]!=(extras|0x80) || frame[1]!=0x10 || frame[2]!=0x87 || frame[3]!=197 || frame[4]!=60 ||
            std::memcmp(frame.data()+5,payload.data(),60)!=0 ||
            std::memcmp(frame.data()+18,payload.data()+13,12)!=0) return false;
    }
    auto withoutBattery=telemetryFrame(payload.data(),0,false,198);
    if ((withoutBattery[0]&0x80)!=0 || withoutBattery[2]!=0 || withoutBattery[3]!=198) return false;
    return true;
}
