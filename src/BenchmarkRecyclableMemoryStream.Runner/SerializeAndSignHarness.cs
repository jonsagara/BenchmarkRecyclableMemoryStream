using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace BenchmarkRecyclableMemoryStream.Runner
{
    [SimpleJob(RuntimeMoniker.NetCoreApp50)]
    [MemoryDiagnoser]
    public class SerializeAndSignHarness
    {
        private WidgetEnvelope WidgetEnvelope { get; } = new WidgetEnvelope();

        public SerializeAndSignHarness()
        {
            // Create 
            foreach (var ix in Enumerable.Range(1, 3000))
            {
                WidgetEnvelope.Widgets.Add(new()
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
        public async Task SerializeToStreamAndSign()
        {
            var signature = await SerializeAndSignHelper.SerializeToMemoryStreamAndSign(WidgetEnvelope);
        }

        [Benchmark]
        public async Task SerializeToRecyclableMemoryStreamAndSign()
        {
            var signature = await SerializeAndSignHelper.SerializeToRecyclableMemoryStreamAndSign(WidgetEnvelope);
        }
    }
}
