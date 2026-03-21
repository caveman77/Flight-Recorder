namespace FlightRecorder.Client.SimConnectMSFS
{
    enum EVENTS
    {
        GENERIC,
        FREEZE_LATITUDE_LONGITUDE,
        FREEZE_ALTITUDE,
        FREEZE_ATTITUDE,
        FRAME,
    }
    enum GROUPS
    {
        GENERIC = 0
    }

    enum DEFINITIONS
    {
        SimState,
        AircraftPositionInitial,
        AircraftPosition,
        AircraftPositionSet,
        AiAircraftPosition,
        AiAircraftPositionSet
    }

    internal enum DATA_REQUESTS
    {
        SIM_STATE,
        AIRCRAFT_POSITION,
        AI_DESPAWN,
        AI_RELEASE,
        CHU_LISTAIRCRAFT,
        CHU_AI_POSITION,

        AI_SPAWN = 10000, // 10000 to 19999
    }
}
