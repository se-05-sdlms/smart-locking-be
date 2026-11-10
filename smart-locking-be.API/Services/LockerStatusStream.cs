using System.Threading.Channels;

namespace smart_locking_be.API.Services;

/// <summary>Fan-out tín hiệu thay đổi tới các kết nối SSE; không giữ dữ liệu nghiệp vụ hay gọi DB.</summary>
public sealed class LockerStatusStream
{
    private readonly object gate = new();
    private readonly Dictionary<string, HashSet<Subscription>> watchers = new(StringComparer.Ordinal);

    public bool HasSubscribers { get { lock (gate) return watchers.Count != 0; } }

    public Subscription Subscribe(string device)
    {
        var subscription = new Subscription(this, device);
        lock (gate)
        {
            if (!watchers.TryGetValue(device, out var list)) watchers[device] = list = [];
            list.Add(subscription);
        }
        return subscription;
    }

    public void Publish(string device)
    {
        lock (gate)
            if (watchers.TryGetValue(device, out var list))
                foreach (var subscription in list) subscription.Signal();
    }

    public sealed class Subscription : IDisposable
    {
        private readonly LockerStatusStream owner;
        private readonly string device;
        // Client chậm chỉ cần đọc snapshot mới nhất; không tích lũy vô hạn các lần đổi cửa.
        private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        internal Subscription(LockerStatusStream owner, string device) { this.owner = owner; this.device = device; }
        internal void Signal() => changes.Writer.TryWrite(true);
        public ValueTask<bool> WaitAsync(CancellationToken ct) => changes.Reader.ReadAsync(ct);
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (owner.watchers.TryGetValue(device, out var list))
                {
                    list.Remove(this);
                    if (list.Count == 0) owner.watchers.Remove(device);
                }
                changes.Writer.TryComplete();
            }
        }
    }
}
