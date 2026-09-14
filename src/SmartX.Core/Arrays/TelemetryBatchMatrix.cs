using SmartX.Core.Telemetry;

namespace SmartX.Core.Arrays;

public sealed class TelemetryBatchMatrix
{
    private readonly object _gate = new();

    // rows are deliberately different lengths, a slow probe sends far fewer readings a minute than a fast meter
    private double[][] _batches;

    private int[] _batchLengths;

    // this one is square because drawing wants it square, every node gets the same number of time slots
    private double[,] _frame;

    private readonly string[] _frameNodes;
    private int _batchCursor;
    private int _frameColumn;

    public TelemetryBatchMatrix(int maxBatches = 64, int nodeSlots = 64, int timeSlots = 48)
    {
        MaxBatches = maxBatches;
        NodeSlots = nodeSlots;
        TimeSlots = timeSlots;

        _batches = new double[maxBatches][];
        _batchLengths = new int[maxBatches];
        _frame = new double[nodeSlots, timeSlots];
        _frameNodes = new string[nodeSlots];
    }

    public int MaxBatches { get; }

    public int NodeSlots { get; }

    public int TimeSlots { get; }

    public int BatchCount
    {
        get { lock (_gate) { return Math.Min(_batchCursor, MaxBatches); } }
    }

    public int AppendBatch(ReadOnlySpan<double> magnitudes)
    {
        lock (_gate)
        {
            var row = _batchCursor % MaxBatches;

            var buffer = new double[magnitudes.Length];
            magnitudes.CopyTo(buffer);

            _batches[row] = buffer;
            _batchLengths[row] = magnitudes.Length;
            _batchCursor++;
            return row;
        }
    }

    public double[] GetBatch(int row)
    {
        lock (_gate)
        {
            if ((uint)row >= (uint)MaxBatches)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return _batches[row] ?? Array.Empty<double>();
        }
    }

    // raw rows become a proper list once the readings have to be searched rather than just collected
    public List<TelemetryPacket<double>> PromoteToCollection(string macAddress, string metric, string unit)
    {
        lock (_gate)
        {
            var total = 0;
            for (var i = 0; i < MaxBatches; i++)
            {
                total += _batchLengths[i];
            }

            var promoted = new List<TelemetryPacket<double>>(total);
            var stamp = DateTimeOffset.UtcNow;
            long sequence = 0;

            for (var row = 0; row < MaxBatches; row++)
            {
                var batch = _batches[row];
                if (batch is null) continue;

                for (var column = 0; column < batch.Length; column++)
                {
                    promoted.Add(new TelemetryPacket<double>(
                        macAddress,
                        metric,
                        batch[column],
                        unit,
                        sequence++,
                        stamp.AddMilliseconds(-(total - sequence) * 10)));
                }

                _batches[row] = null!;
                _batchLengths[row] = 0;
            }

            _batchCursor = 0;
            return promoted;
        }
    }

    public void PushFrameColumn(IReadOnlyList<string> nodeOrder, IReadOnlyList<double> magnitudes)
    {
        lock (_gate)
        {
            var column = _frameColumn % TimeSlots;
            var rows = Math.Min(NodeSlots, Math.Min(nodeOrder.Count, magnitudes.Count));

            for (var row = 0; row < rows; row++)
            {
                _frameNodes[row] = nodeOrder[row];
                _frame[row, column] = magnitudes[row];
            }

            for (var row = rows; row < NodeSlots; row++)
            {
                _frame[row, column] = double.NaN;
            }

            _frameColumn++;
        }
    }

    public (string[] Nodes, double[][] Series) SnapshotFrame()
    {
        lock (_gate)
        {
            var populated = 0;
            for (var row = 0; row < NodeSlots; row++)
            {
                if (!string.IsNullOrEmpty(_frameNodes[row])) populated = row + 1;
            }

            var nodes = new string[populated];
            var series = new double[populated][];
            var start = _frameColumn >= TimeSlots ? _frameColumn % TimeSlots : 0;
            var width = _frameColumn >= TimeSlots ? TimeSlots : _frameColumn;

            for (var row = 0; row < populated; row++)
            {
                nodes[row] = _frameNodes[row];
                var line = new double[width];

                for (var i = 0; i < width; i++)
                {
                    line[i] = _frame[row, (start + i) % TimeSlots];
                }

                series[row] = line;
            }

            return (nodes, series);
        }
    }

    public int FrameCellCount => _frame.Length;
}
