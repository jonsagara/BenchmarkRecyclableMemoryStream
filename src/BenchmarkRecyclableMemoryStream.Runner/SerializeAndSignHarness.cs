using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace BenchmarkRecyclableMemoryStream.Runner
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [MemoryDiagnoser]
    [MarkdownExporterAttribute.GitHub]
    public class SerializeAndSignHarness
    {
        private WidgetEnvelope WidgetEnvelope { get; } = new WidgetEnvelope();

        public SerializeAndSignHarness()
        {
            // Create an object to serialize.
            // This will result in a JSON string of ~1 MB.
            foreach (var ix in Enumerable.Range(1, 3000))
            {
                WidgetEnvelope.Widgets.Add(new Widget
                {
                    Id = Guid.NewGuid(),

                    Property0 = $"{nameof(Widget.Property0)}{ix}",
                    Property1 = $"{nameof(Widget.Property1)}{ix}",
                    Property2 = $"{nameof(Widget.Property2)}{ix}",
                    Property3 = $"{nameof(Widget.Property3)}{ix}",
                    Property4 = $"{nameof(Widget.Property4)}{ix}",
                    Property5 = $"{nameof(Widget.Property5)}{ix}",
                    Property6 = $"{nameof(Widget.Property6)}{ix}",
                    Property7 = $"{nameof(Widget.Property7)}{ix}",
                    Property8 = $"{nameof(Widget.Property8)}{ix}",
                    Property9 = $"{nameof(Widget.Property9)}{ix}",

                    CreatedUtc = DateTime.UtcNow,
                });
            }
        }


        //
        // Benchmark methods
        //

        [Benchmark(Baseline = true)]
        public void SerializeToStringAndSign()
        {
            var signature = SerializeAndSignHelper.SerializeToStringAndSign(WidgetEnvelope);
        }

        [Benchmark]
        public void SerializeToMemoryStreamAndSign()
        {
            var signature = SerializeAndSignHelper.SerializeToMemoryStreamAndSign(WidgetEnvelope);
        }

        [Benchmark]
        public void SerializeToRecyclableMemoryStreamAndSign()
        {
            var signature = SerializeAndSignHelper.SerializeToRecyclableMemoryStreamAndSign(WidgetEnvelope);
        }
    }
}
