# Güvenlik

Evrakçı müvekkil bilgisiyle çalıştığı için güvenlik açıklarını ciddiye alıyoruz.

## Açık bildirmek

Güvenlik açığını **herkese açık issue olarak açmayın.** Bunun yerine:

- GitHub'da [özel güvenlik bildirimi](https://github.com/cahitarslan/evrakci/security/advisories/new) gönderin, ya da
- contact@cahitarslan.com adresine "Evrakçı güvenlik" konulu e-posta yazın.

Açığı, nasıl tekrarlanacağını ve etkisini yazın. Bildirimde gerçek müvekkil verisi kullanmayın. Bildirimi aldığımızı birkaç gün içinde yanıtlarız; düzeltme yayınlanınca isterseniz adınızla teşekkür ederiz.

## Kapsam

Özellikle şunlar önemli:

- `.evrak` dosyalarının şifrelenmesi (AES-256-GCM, PBKDF2-SHA256) ve DPAPI ile saklanan veriler
- Şablon (`.docx`, `.udf`) açarken zararlı dosyalara karşı korumalar (ZIP bombası, makrolu belgeler)
- Güncelleme mekanizması (Velopack, `evrakci.netlify.app/guncelleme`)

## Desteklenen sürümler

Yalnızca son sürüm güncelleme alır. Kurulu uygulama yeni sürümü kendiliğinden indirir.
