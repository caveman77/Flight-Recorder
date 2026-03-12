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
}
