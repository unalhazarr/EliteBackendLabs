# 🔹 Hafta 1-2: Memory & GC — Teori Notları

> **UYARI:** Bunlar ezberlenecek bilgiler değil. Her kavramı "neden böyle?" diye sorgula. Production'da memory dump analiz ederken bu bilgiler işe yarayacak.

---

## ⚠️ Acımasız Gerçek (Oku ve Kendine Sor)

**.NET yazıyorsun. Bu bilgileri bilmeden production kodu yazmak, ehliyet almadan trafiğe çıkmak gibi.**

- **"Bellek yönetimi CLR hallediyor"** demek yetmez. Memory leak yapan kod yazmak .NET'te de mümkün. Event handler, static dictionary, cache sınırı olmayan — hepsi leak kaynağı. Sen bugüne kadar bunların kaçını yazdın, kaçında fark ettin?

- **Gen 0, Gen 1, Gen 2** ne işe yarar diye sorulduğunda "çöp toplama ile ilgili bir şeyler" deyip geçiyorsan, latency-sensitive bir sistemde neden ara ara takıldığını anlayamazsın. GC pause'ları gerçek, ölçülebilir.

- **LOH** — 85KB eşiğini bilmek sadece başlangıç. Büyük buffer'ları `new byte[]` ile mi alıyorsun? `ArrayPool<T>` biliyor musun? Bilmiyorsan, yüksek throughput sistemlerde gereksiz allocation ve GC baskısı yaratıyorsun.

- **Stack vs Heap** — "class heap'te, struct stack'ta" ezberi yetmez. Struct'ı class içinde field yap, heap'e gider. Boxing yap, heap'e gider. Bunu bilmeden "struct kullan performans artsın" diye saçma kararlar verirsin.

- **PerfView / dotMemory** — Hiç kullandın mı? Heap dump aldın mı? "Memory artıyor" diye şikayet edip ama root cause bulamıyorsan, bu araçları öğrenmeyi erteleyemezsin.

**Özet:** Bu hafta sadece not almak değil, bu kavramları gerçek projede kullanmak hedef. Kendi kodunda memory leak olası yerleri tara. Event'lere subscribe edip unsubscribe etmeyen bir yer var mı? Sınırsız büyüyen bir cache? Bul ve düzelt. Yoksa bu notlar duvar süsü kalır.

---

## 1. CLR Nedir?

**Common Language Runtime** — .NET kodunun çalıştığı ortam.

### Basit Tanım
CLR, senin C# kodunu makine koduna (native code) çeviren ve çalıştıran "sanal makine"dir. JVM'in Java için yaptığının .NET versiyonu.

### Önemli Görevleri
| Görev | Açıklama |
|-------|----------|
| **JIT Compilation** | IL (Intermediate Language) → Native code. "Just-in-time" = çalışma anında derleme |
| **Memory Management** | Heap allocation, **GC (Garbage Collection)** — sen manual `free` yapmıyorsun |
| **Type Safety** | Null reference, array bounds check — runtime'da güvenlik |
| **Exception Handling** | Try/catch, stack unwinding |
| **Assembly Loading** | DLL'leri yükler, dependency resolution |

### Kritik Soru
> Neden C++ gibi manuel memory yönetimi yok?  
> **Cevap:** Hata oranını düşürmek. Memory leak, double-free, use-after-free — bunlar C/C++'ta sık. CLR seni bu belalardan korur. Ama **koruyamadığı senaryolar var** — event handler'lar, static collection'lar, unmanaged kaynaklar.

---

## 2. Managed Heap

**Managed** = CLR'ın kontrol ettiği bellek alanı.

### Nasıl Çalışır?
- `new` dediğinde heap'ten blok alır
- Sen pointer döndürmüyorsun — **referans** alıyorsun
- Objenin artık kullanılmadığı anlaşıldığında GC gelir, temizler

### Heap'in Fiziksel Yapısı
```
┌─────────────────────────────────────────────────────┐
│              MANAGED HEAP                            │
├─────────────────┬─────────────────┬─────────────────┤
│   Gen 0          │   Gen 1        │   Gen 2         │  ← Small Object Heap (SOH)
│   (en genç)      │   (orta)       │   (en yaşlı)    │
├─────────────────┴─────────────────┴─────────────────┤
│              LOH (Large Object Heap)                 │  ← 85.000+ byte objeler
└─────────────────────────────────────────────────────┘
```

---

## 3. Stack vs Heap

### Stack
| Özellik | Açıklama |
|---------|----------|
| **Ne saklar?** | Value type'lar (int, struct, bool), metot parametreleri, **referanslar** (pointer) |
| **Boyut** | Küçük, sabit (genelde 1MB default per thread) |
| **Ömür** | Scope bitince otomatik silinir (LIFO) |
| **Hız** | Çok hızlı — sadece pointer move |

### Heap
| Özellik | Açıklama |
|---------|----------|
| **Ne saklar?** | Reference type'lar (class instance'ları) |
| **Boyut** | Büyük, dinamik artabilir |
| **Ömür** | GC karar verir — kimse referans tutmuyorsa toplanır |
| **Hız** | Nispeten yavaş — allocation + GC maliyeti |

### Örnek Zihinsel Model
```csharp
int a = 5;           // Stack: 4 byte
string s = "hi";     // Stack: s referansı (8 byte x64), Heap: "hi" objesi
List<int> list = new List<int>();  // Stack: referans, Heap: List objesi + internal array
```

### Sık Hata
> "Struct heap'te de olabilir!" — Evet. **Boxing** veya **class içinde field** olarak. Ama doğrudan local variable ise stack'tedir.

---

## 4. GC Generations

**Generational GC** — "Genç objeler çoğunlukla hızlı ölür" gözlemiyle optimizasyon.

### Gen 0
- Yeni allocate edilen objeler
- **En sık GC** burada çalışır (birkaç MB dolunca)
- Çok hızlı — küçük alan, az obje

### Gen 1
- Gen 0'dan kurtulan objeler
- Ara tampon — Gen 0 çok sık çalışmasın diye
- Gen 0 GC'de bazen Gen 1 de taranır

### Gen 2
- Uzun ömürlü objeler (singleton, cache, static)
- **En pahalı GC** — tüm heap taranır
- "Full GC" denir

### Promotion
```
Gen 0'da yeni obje → GC → yaşadı → Gen 1'e terfi
Gen 1'de yaşadı → Gen 2'ye terfi
Gen 2'de → orada kalır, bir daha terfi yok
```

### Neden Önemli?
- Gen 2 GC'ler **pause** yaratır — latency kritik sistemlerde sorun
- Uzun ömürlü objeleri Gen 0'da tutmaya çalışma (object pooling vs.)

---

## 5. LOH (Large Object Heap)

### Tanım
**85.000 byte** (≈83 KB) ve üzeri objeler LOH'a gider.

### Neden Ayrı?
- Büyük objeleri kopyalamak (Gen 0→1→2 promotion) **çok maliyetli**
- LOH **compaction yapmaz** (eski .NET'te) — boşluklar kalır, fragmentation riski
- .NET 4.5.1+ `GCSettings.LargeObjectHeapCompactionMode` ile compaction açılabilir

### Hangi Tip Objeler?
- `byte[]` 85KB+
- `string` 85K+ karakter (nadir)
- Büyük collection'lar (List, Dictionary büyük kapasiteyle)
- Custom büyük struct array'leri

### Pratik Tavsiye
- Büyük buffer'ları **pool**'la (ArrayPool<T>)
- Stream'lerde küçük chunk'lar kullan
- `string` birleştirme → StringBuilder (zaten biliyorsundur umarım)

---

## Sonraki Adım: Pratik

Teoriyi okudun. Şimdi `Program.cs`'deki örnekleri çalıştır, her satırı oku, sonra **kendi senaryolarını yaz**. Sadece kopyala-yapıştır yaparsan hiçbir şey öğrenmezsin.

### Çalıştırma

```bash
cd EliteBackendLabs
dotnet run --project MemoryAndGC
# Veya GC log ile:
dotnet run --project MemoryAndGC --launch-profile "MemoryAndGC-WithGcLog"
# PowerShell'de env ile:
$env:DOTNET_GCLogFile="MemoryAndGC.gc.log"; dotnet run --project MemoryAndGC
```

### PerfView / dotMemory

1. **PerfView**: Ücretsiz, Microsoft. Collect > Collect ile CPU/memory trace al.
2. **dotMemory**: JetBrains, trial. Heap snapshot al, retained size ile leak kaynağını bul.
