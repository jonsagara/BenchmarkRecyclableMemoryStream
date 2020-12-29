using BenchmarkDotNet.Running;

namespace BenchmarkRecyclableMemoryStream.Runner
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<SerializeAndSignHarness>();
        }
    }
}
