
using System;

namespace ps_activity_insights
{
    public enum eventType { BuildDone, ChangeTab, SaveFile, Shutdown, Typing };
    public class Event
    {
        public eventType eventType { get; set; }
        public string filePath { get; set; } = "N/A";
        public Int64 eventDate { get; set; }
        public string editor { get; set; } = "Visual Studio";

        public Event(eventType type, string filePath)
        {
            this.eventType = type;
            this.filePath = filePath;
            getUnixTimestamp();
        }
        public Event(eventType type)
        {
            this.eventType = type;
            getUnixTimestamp();
        }

        private void getUnixTimestamp()
        {
            var now = DateTime.Now;
            var unixTimestamp = ((DateTimeOffset)now).ToUnixTimeMilliseconds();
            this.eventDate = unixTimestamp;
        }
    }
}
