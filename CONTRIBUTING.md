# Katkı rehberi

Evrakçı'ya katkı vermek istediğiniz için teşekkürler. Arabulucu, avukat ya da geliştirici olmanız fark etmez; her katkı işe yarar.

## Kişisel veri kuralı

Bu proje müvekkil bilgisiyle çalışan bir uygulama. Issue, pull request, ekran görüntüsü ya da örnek dosyada **gerçek isim, TCKN, IBAN, adres, telefon, dosya numarası ya da gerçek bir `.evrak` dosyası paylaşmayın.** Örnekler için uydurma veriler kullanın (ör. "Ayşe YILMAZ", TCKN `12345678950`, IBAN `TR84 0001 0000 0001 2345 6789 01`). Gerçek veri içeren katkılar silinir.

## Nasıl katkı verebilirsiniz

- **Hata bildirimi:** [Yeni issue](https://github.com/cahitarslan/evrakci/issues/new/choose) açın; ne yaptığınızı, ne beklediğinizi ve ne olduğunu yazın. Uygulama sürümünü (Şablonlar ekranının altında) ekleyin.
- **Şablon metni düzeltmesi:** Hazır setlerdeki hukuki metinlerle ilgili öneriler en değerli katkılardan biridir. Hangi set, hangi belge, hangi cümle ve dayanağı (kanun maddesi, yönetmelik, uygulama) ile birlikte yazın.
- **Yeni arabuluculuk türü ya da belge:** Önce bir issue açıp konuşalım; sonra metinler `tools/Evrakci.SablonUretici/Program.cs` içine eklenir.
- **Kod:** Küçük düzeltmeler için doğrudan pull request açabilirsiniz. Büyük değişiklikler için önce issue açın.

## Geliştirme ortamı

Gerekenler: Windows 10/11 ve [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/cahitarslan/evrakci.git
cd evrakci
dotnet test tests\Evrakci.Tests            # bütün testler geçmeli
dotnet run --project src\Evrakci.App       # uygulamayı çalıştırır
```

Hazır şablonların `.docx` dosyalarını elle değiştirmeyin. Metinleri `tools/Evrakci.SablonUretici/Program.cs` içinde değiştirip şu komutu çalıştırın:

```powershell
dotnet run --project tools\Evrakci.SablonUretici
```

Bu komut `Sablonlar/Hazır` klasörünü yeniden üretir; uygulama derlenirken bu klasörü içine gömer.

## Pull request kontrol listesi

- [ ] `dotnet test tests\Evrakci.Tests` geçiyor.
- [ ] Davranış değiştiyse test eklendi ya da güncellendi.
- [ ] Arayüz metinleri Türkçe ve uygulamanın geri kalanıyla aynı üslupta.
- [ ] Gerçek kişi verisi yok.
- [ ] Kullanıcıyı ilgilendiren bir değişiklikse README güncellendi.

## Kod düzeni

- Motor (`src/Evrakci.Core`) arayüzden bağımsızdır; WPF'e bağımlılık eklemeyin.
- Kod, yorumlar ve test adları İngilizce; kullanıcıya görünen metinler Türkçedir.
- Mevcut dosyaların biçimine uyun (`dotnet format` kullanabilirsiniz).

## Lisans

Gönderdiğiniz katkılar projenin lisansı olan [AGPL-3.0](LICENSE) ile lisanslanır.
