namespace ps_activity_insights_shared
{
    using System;
    public enum EventType
    {
        BuildDone,
        ChangeTab,
        SaveFile,
        Shutdown,
        Typing
    };

    public class Event
    {
        public EventType EventType { get; set; }
        public string FilePath { get; set; } = "N/A";
        public long EventDate { get; set; }
        public string Editor { get; set; } = "Visual Studio";

        public Event(EventType type, string filePath)
        {
            EventType = type;
            FilePath = filePath;
            GetUnixTimestamp();
        }

        public Event(EventType type)
        {
            EventType = type;
            GetUnixTimestamp();
        }

        private void GetUnixTimestamp()
        {
            var now = DateTime.Now;
            var unixTimestamp = ((DateTimeOffset)now).ToUnixTimeMilliseconds();
            EventDate = unixTimestamp;
        }
    }
}
