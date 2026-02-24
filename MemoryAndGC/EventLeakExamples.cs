// ============================================================================
// EVENT HANDLER LEAK - Sınıf örnekleri
// Bu dosyada olma sebebi: C# top-level statements, type tanımlarından önce gelmeli.
// Sınıfları ayrı dosyada tutuyoruz ki Program.cs tamamen top-level kalsın.
// ============================================================================

/// <summary>
/// Uzun ömürlü publisher. Event'in invocation list'inde tuttuğu her handler,
/// o handler'ın hedef objesini (subscriber) de yaşatır.
/// </summary>
internal class EventPublisher
{
    public event EventHandler? SomethingHappened;
    public void Raise() => SomethingHappened?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Kısa ömürlü gibi görünen ama publisher'a subscribe edildiği için
/// publisher yaşadığı sürece GC'ye gidemeyen subscriber.
/// Çözüm: IDisposable + -= SomethingHappened
/// </summary>
internal class EventSubscriber
{
    private readonly byte[] _data = new byte[10000]; // Bellek tüketiyor
    public EventSubscriber(EventPublisher pub)
    {
        // LEAK: pub.SomethingHappened += ... ile subscriber'ı publisher'a bağladık
        // Publisher hiç dispose olmazsa, subscriber da asla GC'ye gitmez
        pub.SomethingHappened += OnSomethingHappened;
    }
    private void OnSomethingHappened(object? s, EventArgs e) { }
}
