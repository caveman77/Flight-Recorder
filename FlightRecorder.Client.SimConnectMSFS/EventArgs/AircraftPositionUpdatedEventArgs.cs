using System;

namespace FlightRecorder.Client.SimConnectMSFS
{
    public class AircraftPositionUpdatedEventArgs : EventArgs
    {
        public AircraftPositionUpdatedEventArgs(uint dwObjectID, AircraftPositionStruct position)
        {
            Position = position;
            this.dwObjectID = dwObjectID;
        }

        public AircraftPositionStruct Position { get; }

        public uint dwObjectID { get; }
    }


    public class AiAircraftPositionUpdatedEventArgs : EventArgs
    {
        public AiAircraftPositionUpdatedEventArgs(uint dwObjectID, AiAircraftPositionStruct position)
        {
            Position = position;
            this.dwObjectID = dwObjectID;
        }

        public AiAircraftPositionStruct Position { get; }

        public uint dwObjectID { get; }
    }
}
