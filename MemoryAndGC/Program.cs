/*
 * Memory & GC Pratik Lab - Hafta 1-2
 *
 * ÇALIŞTIRMADAN ÖNCE:
 * 1. GC log almak için: dotnet run -- --gc-log
 * 2. PerfView/dotMemory ile izlemek için: Debug modda çalıştır, istediğin örnek numarasını seç
 *
 * Bu dosyadaki her örnek KASITLI olarak belirli bir kavramı gösteriyor.
 * Sadece çalıştırıp geçme - her satırı oku, neden böyle yazıldığını anla.
 */

// ============================================================================
// ÖRNEK 1: MEMORY LEAK SENARYOSU
// ============================================================================
// LEAK NASIL OLUŞUR? GC "referans tutuluyor" gördüğü objeyi toplayamaz.
// En klasik sebep: static/uzun ömürlü collection'a sürekli ekleme, hiç temizlememe.
// ============================================================================

// Bu liste UYGULAMA BOYUNCA yaşar (top-level scope). İçine eklenen her şey de yaşar.
// Event handler leak'inin aynı mantığı: Publisher uzun ömürlü, handler'ı tutuyor.
List<byte[]> memoryLeakBucket = new();

void RunMemoryLeakScenario()
{
    Console.WriteLine("=== MEMORY LEAK SENARYOSU ===\n");

    // ADIM 1: Başlangıç memory durumu
    Console.WriteLine($"[Başlangıç] Gen0: {GC.CollectionCount(0)}, Gen1: {GC.CollectionCount(1)}, Gen2: {GC.CollectionCount(2)}");

    // ADIM 2: Her iterasyonda 100KB allocation - AMA referans static listeye ekleniyor
    // GC bu objeleri TOPLAYAMAZ çünkü _memoryLeakBucket hala referans tutuyor
    for (int i = 0; i < 100; i++)
    {
        byte[] chunk = new byte[100_000]; // 100KB - LOH'a gitmez (85KB altı)
        memoryLeakBucket.Add(chunk);      // ← İŞTE LEAK: Hiçbir zaman Remove/Clear yok
    }

    // ADIM 3: GC'ye "temizle" desek bile temizleyemez - referanslar duruyor
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Console.WriteLine($"[Leak sonrası] Gen0: {GC.CollectionCount(0)}, Heap'te ~{100 * 100_000 / 1024} KB sıkışmış durumda");
    Console.WriteLine("dotMemory/PerfView ile heap dump al - memoryLeakBucket'ı göreceksin.\n");
}

// ============================================================================
// ÖRNEK 2: BÜYÜK OBJE ALLOCATION (LOH)
// ============================================================================
// 85.000 byte üzeri objeler LOH'a gider. LOH compaction yapmaz (default).
// Allocation sayısını ve Gen koleksiyonlarını izle - LOH farklı davranır.
// ============================================================================

void RunLargeObjectAllocation()
{
    Console.WriteLine("=== BÜYÜK OBJE ALLOCATION (LOH) ===\n");

    // ADIM 1: LOH eşiği - 85.000 byte. Bir byte fazlası LOH'a gönderir.
    const int sohMaxSize = 84_999;   // Small Object Heap - son sınır
    const int lohMinSize = 85_000;   // LOH - ilk sınır

    Console.WriteLine($"[SOH] {sohMaxSize} byte allocation - Gen 0'a gider");
    byte[] smallArray = new byte[sohMaxSize];
    Console.WriteLine($"    Gen0 collections: {GC.CollectionCount(0)}");

    // ADIM 2: 1 byte ekle - artık LOH
    Console.WriteLine($"\n[LOH] {lohMinSize} byte allocation - LOH'a gider");
    byte[] largeArray = new byte[lohMinSize];
    // LOH objeleri Gen 2'de sayılır - direkt oraya gider, Gen 0'dan geçmez
    Console.WriteLine($"    Gen0: {GC.CollectionCount(0)}, Gen2: {GC.CollectionCount(2)}");

    // ADIM 3: Bir sürü büyük obje - LOH'u doldur
    Console.WriteLine("\n[LOH doldurma] 50 adet 1MB allocation...");
    var largeObjects = new List<byte[]>();
    for (int i = 0; i < 50; i++)
    {
        largeObjects.Add(new byte[1_000_000]); // 1MB each = 50MB LOH
    }
    Console.WriteLine($"    Toplam ~50MB LOH. Gen2 collections: {GC.CollectionCount(2)}");

    // ADIM 4: Referansları bırak - GC toplayabilir
    largeObjects.Clear();
    largeObjects = null!;
    GC.Collect(2, GCCollectionMode.Forced);
    Console.WriteLine("    Referanslar bırakıldı, Gen2 GC tetiklendi.\n");
}

// ============================================================================
// ÖRNEK 3: GC COLLECT LOGLARI
// ============================================================================
// Environment variable ile GC log'u açılır. Çıktı dosyasına yazılır.
// dotnet run -- --gc-log ile çalıştır, MemoryAndGC.gc.log dosyası oluşur.
// ============================================================================

void RunGcLoggingDemo()
{
    Console.WriteLine("=== GC LOGGING DEMO ===\n");
    Console.WriteLine("GC log açıksa (--gc-log ile çalıştırdıysan) dosyaya yazılıyor.");
    Console.WriteLine("Örnek allocation'lar yapılıyor...\n");

    // Bir miktar allocation - GC tetikleyecek
    for (int i = 0; i < 1000; i++)
    {
        _ = new byte[1000];
    }
    GC.Collect(0);

    for (int i = 0; i < 100; i++)
    {
        _ = new byte[10_000];
    }
    GC.Collect(1);

    Console.WriteLine("Gen0 ve Gen1 GC tetiklendi.");
    Console.WriteLine("Log dosyasını incele: MemoryAndGC.gc.log (proje klasöründe)");
    Console.WriteLine("PerfView: File > User Command > GCCollectOnly veya Heap Snapshot\n");
}

// ============================================================================
// ÖRNEK 4: EVENT HANDLER LEAK (Klasik .NET Hatası)
// ============================================================================
// Publisher uzun ömürlü, subscriber kısa ömürlü ise:
// Publisher event'i tuttuğu sürece subscriber GC'ye gitmez = LEAK
// EventPublisher / EventSubscriber -> EventLeakExamples.cs dosyasında
// ============================================================================

void RunEventLeakScenario()
{
    Console.WriteLine("=== EVENT HANDLER LEAK ===\n");

    var publisher = new EventPublisher();

    // Her döngüde yeni subscriber, ama hepsi publisher'a bağlı
    // Subscriber'ları "unsubscribe" etmediğimiz için hepsi yaşamaya devam eder
    for (int i = 0; i < 100; i++)
    {
        var sub = new EventSubscriber(publisher);
        // sub scope dışına çıkınca eligible olur AMA publisher hala handler'ı tutuyor
        // Çözüm: IDisposable ile -= SomethingHappened
    }

    GC.Collect();
    Console.WriteLine("100 subscriber eklendi, hiçbiri unsubscribe edilmedi.");
    Console.WriteLine("Publisher yaşadığı sürece heap'te kalacaklar.\n");
}

// ============================================================================
// ANA GİRİŞ NOKTASI
// ============================================================================

// Hangi örneği çalıştırmak istediğini buradan seç
// GC LOG: Process BAŞLAMADAN ÖNCE set edilmeli! Runtime'da set edilmez.
// PowerShell: $env:DOTNET_GCLogFile="MemoryAndGC.gc.log"; dotnet run
// Veya: launchSettings.json'daki "MemoryAndGC-WithGcLog" profilini kullan (VS / dotnet run)

Console.WriteLine("Hangi örneği çalıştır? (1=Leak, 2=LOH, 3=GC Log, 4=Event Leak, 5=Tümü)");
string? input = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Console.ReadLine();
int choice = int.TryParse(input, out int c) ? c : 1;

switch (choice)
{
    case 1: RunMemoryLeakScenario(); break;
    case 2: RunLargeObjectAllocation(); break;
    case 3: RunGcLoggingDemo(); break;
    case 4: RunEventLeakScenario(); break;
    case 5:
        RunMemoryLeakScenario();
        RunLargeObjectAllocation();
        RunGcLoggingDemo();
        RunEventLeakScenario();
        break;
    default: RunMemoryLeakScenario(); break;
}

Console.WriteLine("\n--- Lab tamamlandı. PerfView veya dotMemory ile heap'i incele. ---");
