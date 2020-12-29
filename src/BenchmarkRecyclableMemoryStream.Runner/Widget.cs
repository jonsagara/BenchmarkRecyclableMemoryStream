using System;
using System.Collections.Generic;

namespace BenchmarkRecyclableMemoryStream.Runner
{
    public class WidgetEnvelope
    {
        public List<Widget> Widgets { get; private set; } = new List<Widget>();
    }

    public class Widget
    {
        public Guid Id { get; set; }

        public string Property0 { get; set; } = null!;
        public string Property1 { get; set; } = null!;
        public string Property2 { get; set; } = null!;
        public string Property3 { get; set; } = null!;
        public string Property4 { get; set; } = null!;
        public string Property5 { get; set; } = null!;
        public string Property6 { get; set; } = null!;
        public string Property7 { get; set; } = null!;
        public string Property8 { get; set; } = null!;
        public string Property9 { get; set; } = null!;

        public DateTime CreatedUtc { get; set; }
    }
}
