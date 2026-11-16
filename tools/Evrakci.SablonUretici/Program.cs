// Builds the ready-made template sets in Sablonlar\Hazır (one folder per kind of mediation file).
// Run from the repository:  dotnet run --project tools\Evrakci.SablonUretici
// The app embeds that folder; change the texts here and run again rather than editing the .docx files.

using System.Text;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates.Docx;

var review = args.Contains("--inceleme");
var root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindRoot();
var outDir = Path.Combine(root, "Sablonlar", "Hazır");
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
foreach (var spec in Specs.All) SetWriter.Write(spec, Path.Combine(outDir, spec.Folder));
Console.WriteLine($"{Specs.All.Count} set yazıldı: {outDir}");
if (review)
{
    var path = Path.Combine(root, "Şablon Metinleri (inceleme).docx");
    File.WriteAllBytes(path, Review.Build(outDir));
    Console.WriteLine($"İnceleme belgesi: {path}");
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Evrakci.slnx"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Evrakci.slnx bulunamadı.");
}

/// <summary>One kind of mediation file.</summary>
sealed class Spec
{
    public required string Folder { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    /// <summary>Statute that makes mediation a precondition of the lawsuit; null for voluntary mediation.</summary>
    public string? DavaSartiKanun { get; init; }
    public required string Role1 { get; init; }
    public required string Role2 { get; init; }
    public required string Konu { get; init; }
    public string Sure { get; init; } = "üç hafta içinde sonuçlandırır; bu süre zorunlu hâllerde arabulucu tarafından en fazla bir hafta uzatılabilir";
    /// <summary>Agreement clauses specific to the kind, written before the payment clauses.</summary>
    public required Action<DocxBuilder> Terms { get; init; }
    /// <summary>Waiver clause after the payment; may use variables.</summary>
    public required string Feragat { get; init; }
    /// <summary>"Agreed amount" is the sum of these fields (null: entered by hand).</summary>
    public string? AmountComputed { get; init; }
    public string AmountLabel { get; init; } = "Mutabık kalınan toplam tutar";
    public string? Taraf2Yetkili { get; init; }
    public List<string> ExtraGroups { get; init; } = new();
    public Dictionary<string, FieldDefinition> Fields { get; init; } = new();
    public List<CheckDefinition> Checks { get; init; } = new();
    public bool DavaSarti => DavaSartiKanun is not null;

    // Texts follow the wording of real mediation documents (no names or data from them are kept). When
    // DavaSartiCumlesi is set, the invitation letter, the first session record and the final records
    // (agreement / no agreement) are written in that form; other kinds keep the shorter texts.
    /// <summary>Sentence of the invitation letter that names the statute making mediation compulsory.</summary>
    public string? DavaSartiCumlesi { get; init; }
    public bool OrnekMetin => DavaSartiCumlesi is not null;
    public string DavetBasligi { get; init; } = "DAVA ŞARTI ARABULUCULUK SÜRECİNE";
    /// <summary>Paragraphs of the invitation letter after the statute (time limits, employer rules …).</summary>
    public List<string> DavetEk { get; init; } = new();
    /// <summary>Kind-specific sentences appended to the fee paragraph of the invitation letter.</summary>
    public string? UcretEk { get; init; }
    /// <summary>Kind-specific sentence appended to the representation paragraph of the invitation letter.</summary>
    public string? TemsilEk { get; init; }
    /// <summary>Kind-specific sentence appended to the "not attending the first meeting" paragraph.</summary>
    public string? KatilmamaEk { get; init; }
    /// <summary>Statute named next to 6325 m.17 in the closing sentence of the final records.</summary>
    public string? SonTutanakEkKanun { get; init; }
}

static class F
{
    public static FieldDefinition Metin(string group, string label, string? hint = null, string? def = null, bool optional = false,
        string? transform = null, string? pattern = null, string? from = null) => new()
    {
        Type = "Metin", Group = group, Label = label, Hint = hint, Default = def, Transform = transform, Pattern = pattern,
        DefaultFrom = from, RemoveParagraphIfBlank = optional, BlankByDefault = optional && def is null && from is null,
    };

    public static FieldDefinition Para(string group, string label, bool optional = false, string? computed = null, string? def = null,
        bool blank = true) => new()
    {
        Type = "Para", Group = group, Label = label, Currency = "TRY", Computed = computed, Default = def,
        RemoveParagraphIfBlank = optional, BlankByDefault = optional && blank,
    };

    public static FieldDefinition Tarih(string group, string label, string? from = null, bool optional = false, string? hint = null) => new()
    {
        Type = "Tarih", Group = group, Label = label, DefaultFrom = from, Hint = hint,
        RemoveParagraphIfBlank = optional, BlankByDefault = optional,
    };

    public static FieldDefinition Of(string type, string group, string label, bool optional = false, string? hint = null) => new()
    {
        Type = type, Group = group, Label = label, Hint = hint, RemoveParagraphIfBlank = optional, BlankByDefault = optional,
    };
}

static class Specs
{
    const string Konu = "Uyuşmazlık";

    public static readonly List<Spec> All =
    [
        new()
        {
            Folder = "İşçi - İşveren", Name = "İşçi – İşveren Arabuluculuğu", Category = "İşçi – İşveren",
            Description = "İşçilik alacakları ve işe iade talepleri (7036 sayılı İş Mahkemeleri Kanunu m.3).",
            DavaSartiKanun = "7036 sayılı İş Mahkemeleri Kanunu m.3",
            Role1 = "Başvurucu İşçi", Role2 = "İşveren", Taraf2Yetkili = "İşveren yetkilisi",
            Konu = "Kıdem tazminatı, ihbar tazminatı, ücret alacağı, yıllık ücretli izin alacağı, fazla çalışma, hafta tatili, ulusal bayram ve genel tatil ücreti alacakları, ikramiye/prim alacağı ile maddi ve manevi tazminat talepleri dâhil tüm işçilik alacakları ve işe iade talebi",
            AmountComputed = "KIDEM TAZMİNATI + İHBAR TAZMİNATI + ÜCRET ALACAĞI + YILLIK İZİN ÜCRETİ + DİĞER İŞÇİLİK ALACAKLARI",
            ExtraGroups = ["İş İlişkisi", "Alacak Kalemleri"],
            Fields =
            {
                ["İŞE GİRİŞ TARİHİ"] = F.Tarih("İş İlişkisi", "İşe giriş tarihi"),
                ["FESİH TARİHİ"] = F.Tarih("İş İlişkisi", "İş sözleşmesinin sona erdiği tarih"),
                ["KIDEM TAZMİNATI"] = F.Para("Alacak Kalemleri", "Kıdem tazminatı", optional: true, blank: false),
                ["İHBAR TAZMİNATI"] = F.Para("Alacak Kalemleri", "İhbar tazminatı", optional: true, blank: false),
                ["ÜCRET ALACAĞI"] = F.Para("Alacak Kalemleri", "Ücret alacağı", optional: true, blank: false),
                ["ÜCRET DÖNEMİ"] = F.Metin("Alacak Kalemleri", "Ücret alacağının dönemi", "Örn. 2026 yılı Eylül ayı"),
                ["YILLIK İZİN ÜCRETİ"] = F.Para("Alacak Kalemleri", "Yıllık ücretli izin alacağı", optional: true, blank: false),
                ["DİĞER İŞÇİLİK ALACAKLARI"] = F.Para("Alacak Kalemleri", "Diğer alacaklar (fazla çalışma, hafta tatili vb.)", optional: true),
            },
            Checks = [new() { Kind = "dateOrder", Fields = ["İŞE GİRİŞ TARİHİ", "FESİH TARİHİ", "BAŞVURU TARİHİ"], Message = "İşe giriş, fesih ve başvuru tarihleri sırasız." }],
            Terms = b =>
            {
                b.Para("Taraflar arasındaki iş ilişkisinin {{{İŞE GİRİŞ TARİHİ}}} tarihinde başladığı ve {{{FESİH TARİHİ}}} tarihinde sona erdiği hususunda taraflar mutabıktır. Taraflar, işçinin işveren nezdindeki çalışmasından doğan alacaklarına karşılık aşağıdaki tutarlar üzerinde anlaşmışlardır:");
                b.Item("–", "Kıdem tazminatı: {{{KIDEM TAZMİNATI}}} ({{{KIDEM TAZMİNATI.Yazıyla}}})");
                b.Item("–", "İhbar tazminatı: {{{İHBAR TAZMİNATI}}} ({{{İHBAR TAZMİNATI.Yazıyla}}})");
                b.Item("–", "{{{ÜCRET DÖNEMİ}}} ücret alacağı: {{{ÜCRET ALACAĞI}}} ({{{ÜCRET ALACAĞI.Yazıyla}}})");
                b.Item("–", "Yıllık ücretli izin alacağı: {{{YILLIK İZİN ÜCRETİ}}} ({{{YILLIK İZİN ÜCRETİ.Yazıyla}}})");
                b.Item("–", "Diğer işçilik alacakları: {{{DİĞER İŞÇİLİK ALACAKLARI}}} ({{{DİĞER İŞÇİLİK ALACAKLARI.Yazıyla}}})");
            },
            Feragat = "İşçi {{{TARAF1 ADI}}}, işbu anlaşmada belirtilen ödemelerin eksiksiz yapılması ile işveren nezdindeki çalışmasından kaynaklanan, arabuluculuk sürecinde müzakere edilen tüm işçilik alacakları ve tazminat talepleri ile işe iade talebi yönünden işverenden başkaca herhangi bir hak ve alacağının kalmadığını, bu hususlarda dava açmayacağını kabul ve beyan etmiştir.",
            DavaSartiCumlesi = "7036 sayılı İş Mahkemeleri Kanununun 3 üncü maddesi uyarınca kanuna, bireysel veya toplu iş sözleşmesine dayanan işçi veya işveren alacağı ve tazminatı ile işe iade talebiyle açılan davalarda, arabulucuya başvurulmuş olması dava şartıdır.",
            DavetEk = ["Asıl işveren-alt işveren ilişkisinin varlığı hâlinde işe iade talebiyle arabulucuya başvurulduğunda, anlaşmanın gerçekleşebilmesi için işverenlerin arabuluculuk görüşmelerine birlikte katılmaları ve iradelerinin birbirine uygun olması aranır."],
            UcretEk = "İşe iade talebiyle yapılan görüşmelerde tarafların anlaşmaları durumunda, arabulucuya ödenecek ücretin belirlenmesinde işçiye işe başlatılmaması hâlinde ödenecek tazminat miktarı ile çalıştırılmadığı süre için ödenecek ücret ve diğer haklarının toplamı, Tarifenin İkinci Kısmı uyarınca üzerinde anlaşılan miktar olarak kabul edilir.",
            TemsilEk = "İşverenin yazılı belgeyle yetkilendirdiği çalışanı da görüşmelerde işvereni temsil edebilir ve son tutanağı imzalayabilir.",
            SonTutanakEkKanun = "7036 sayılı İş Mahkemeleri Kanunu m. 3",
        },
        new()
        {
            Folder = "Ticari", Name = "Ticari Uyuşmazlık Arabuluculuğu", Category = "Ticari",
            Description = "Konusu bir miktar paranın ödenmesi olan alacak ve tazminat talepleri (6102 sayılı TTK m.5/A).",
            DavaSartiKanun = "6102 sayılı Türk Ticaret Kanunu m.5/A",
            Role1 = "Başvurucu", Role2 = "Karşı Taraf", Taraf2Yetkili = "Şirket yetkilisi",
            Konu = "Ticari ilişkiden kaynaklanan alacak talebi",
            Sure = "altı hafta içinde sonuçlandırır; bu süre zorunlu hâllerde arabulucu tarafından en fazla iki hafta uzatılabilir",
            ExtraGroups = ["Ticari İlişki"],
            Fields =
            {
                ["DAYANAK BELGE"] = F.Metin("Ticari İlişki", "Alacağın dayanağı", "Örn. 12.03.2026 tarihli ve 123 numaralı fatura", optional: true),
                ["TALEP EDİLEN TUTAR"] = F.Para("Ticari İlişki", "Başvuruda talep edilen tutar", optional: true),
            },
            Terms = b =>
            {
                b.Para("Başvurucu, {{{DAYANAK BELGE}}} kapsamındaki ticari ilişkiden kaynaklanan alacağı için karşı taraftan {{{TALEP EDİLEN TUTAR}}} talep etmiştir.");
                b.Para("Taraflar, uyuşmazlık konusu alacağın asıl alacak, faiz ve tüm fer’ileri dâhil olmak üzere aşağıda belirtilen tutar ve vadelerde ödenmesi hususunda anlaşmışlardır.");
            },
            Feragat = "{{{ÖDEME ALACAK TARAF}}}, işbu anlaşmada belirtilen ödemelerin eksiksiz yapılması ile uyuşmazlık konusu alacak ve fer’ileri yönünden karşı taraftan başkaca bir talebinin kalmadığını, bu konuda başlatılmış takip ve dava varsa bunlardan feragat edeceğini kabul ve beyan etmiştir.",
            DavaSartiCumlesi = "6102 sayılı Türk Ticaret Kanununun 5/A maddesi uyarınca ticari davalardan, konusu bir miktar paranın ödenmesi olan alacak ve tazminat talepleri hakkında dava açılmadan önce arabulucuya başvurulmuş olması dava şartıdır.",
            DavetBasligi = "TİCARİ DAVA ŞARTI ARABULUCULUK SÜRECİNE",
            DavetEk = ["Dava açılmadan önce ihtiyati tedbir kararı verilmesi hâlinde 6100 sayılı Hukuk Muhakemeleri Kanununun 397 nci maddesinin birinci fıkrasında, ihtiyati haciz kararı verilmesi hâlinde ise 2004 sayılı İcra ve İflas Kanununun 264 üncü maddesinin birinci fıkrasında düzenlenen dava açma süresi, arabuluculuk bürosuna başvurulmasından son tutanağın düzenlendiği tarihe kadar işlemez."],
            TemsilEk = "Taraflardan birinin idare olması hâlinde arabuluculuk görüşmelerinde idareyi, üst yönetici tarafından belirlenen iki üye ile hukuk birimi amiri veya onun belirleyeceği bir avukat ya da hukuk müşavirinden oluşan komisyon temsil eder; komisyon kendisini vekil ile temsil ettiremez.",
        },
        new()
        {
            Folder = "Tüketici", Name = "Tüketici Uyuşmazlığı Arabuluculuğu", Category = "Tüketici",
            Description = "Tüketici hakem heyetinin görev alanı dışında kalan tüketici uyuşmazlıkları (6502 sayılı Kanun m.73/A).",
            DavaSartiKanun = "6502 sayılı Tüketicinin Korunması Hakkında Kanun m.73/A",
            Role1 = "Tüketici", Role2 = "Satıcı / Sağlayıcı", Taraf2Yetkili = "Firma yetkilisi",
            Konu = "Satın alınan mal / hizmetin ayıplı olmasından kaynaklanan bedel iadesi talebi",
            ExtraGroups = ["Mal / Hizmet"],
            Fields =
            {
                ["MAL HİZMET"] = F.Metin("Mal / Hizmet", "Uyuşmazlık konusu mal veya hizmet", "Örn. X marka buzdolabı"),
                ["SATIN ALMA TARİHİ"] = F.Tarih("Mal / Hizmet", "Satın alma / sözleşme tarihi"),
                ["SATICI YÜKÜMLÜLÜĞÜ"] = F.Metin("Mal / Hizmet", "Satıcının ayrıca üstlendiği yükümlülük", "Örn. ürünü 7 gün içinde teslim almak", optional: true),
            },
            Terms = b =>
            {
                b.Para("Tüketici, {{{SATIN ALMA TARİHİ}}} tarihinde satın aldığı {{{MAL HİZMET}}} nedeniyle karşı taraftan talepte bulunmuştur. Taraflar uyuşmazlığın aşağıdaki şekilde çözümü hususunda anlaşmışlardır.");
                b.Para("Satıcı / sağlayıcı ayrıca {{{SATICI YÜKÜMLÜLÜĞÜ}}} yükümlülüğünü üstlenmiştir.");
            },
            Feragat = "Tüketici {{{TARAF1 ADI}}}, işbu anlaşmada belirtilen ödeme ve yükümlülüklerin yerine getirilmesi ile uyuşmazlık konusu mal / hizmet nedeniyle karşı taraftan başkaca herhangi bir talebinin kalmadığını kabul ve beyan etmiştir.",
            DavaSartiCumlesi = "6502 sayılı Tüketicinin Korunması Hakkında Kanunun 73/A maddesi uyarınca tüketici mahkemelerinde görülen uyuşmazlıklarda dava açılmadan önce arabulucuya başvurulmuş olması dava şartıdır.",
            UcretEk = "Tüketicinin ödemesi gereken arabuluculuk ücreti, Adalet Bakanlığı bütçesinden karşılanır. Ancak belirtilen hâlde tüketicinin ödeyeceği arabuluculuk ücreti, Arabuluculuk Asgari Ücret Tarifesinin eki Arabuluculuk Ücret Tarifesinin Birinci Kısmına göre iki saatlik ücret tutarını geçemez.",
            KatilmamaEk = "Bu yaptırım tüketici aleyhine uygulanmaz.",
        },
        new()
        {
            Folder = "Kira", Name = "Kira Uyuşmazlığı Arabuluculuğu", Category = "Kira",
            Description = "Kira ilişkisinden kaynaklanan uyuşmazlıklar (6325 sayılı Kanun m.18/B; ilamsız icra yoluyla tahliye hariç).",
            DavaSartiKanun = "m.18/B",
            Role1 = "Kiraya Veren", Role2 = "Kiracı",
            Konu = "Kira bedelinin tespiti / kira alacağı ve tahliye talebi",
            ExtraGroups = ["Kiralanan"],
            Fields =
            {
                ["KİRALANAN ADRES"] = F.Metin("Kiralanan", "Kiralananın adresi"),
                ["KİRA SÖZLEŞMESİ TARİHİ"] = F.Tarih("Kiralanan", "Kira sözleşmesinin başlangıç tarihi"),
                ["YENİ KİRA BEDELİ"] = F.Para("Kiralanan", "Yeni aylık kira bedeli", optional: true),
                ["YENİ KİRA BAŞLANGICI"] = F.Tarih("Kiralanan", "Yeni kira bedelinin geçerli olacağı tarih", optional: true),
                ["TAHLİYE TARİHİ"] = F.Tarih("Kiralanan", "Kiracının tahliye edeceği tarih", optional: true),
            },
            Checks = [new() { Kind = "blankTogether", Fields = ["YENİ KİRA BEDELİ", "YENİ KİRA BAŞLANGICI"], Message = "Yeni kira bedeli ve başlangıç tarihi birlikte girilmeli ya da birlikte boş bırakılmalı." }],
            Terms = b =>
            {
                b.Para("Taraflar arasında {{{KİRALANAN ADRES}}} adresindeki taşınmaza ilişkin {{{KİRA SÖZLEŞMESİ TARİHİ}}} başlangıç tarihli kira sözleşmesi bulunmaktadır. Taraflar aşağıdaki hususlarda anlaşmışlardır:");
                b.Item("–", "{{{YENİ KİRA BAŞLANGICI}}} tarihinden itibaren aylık kira bedeli {{{YENİ KİRA BEDELİ}}} ({{{YENİ KİRA BEDELİ.Yazıyla}}}) olarak uygulanacaktır.");
                b.Item("–", "Kiracı, kiralananı en geç {{{TAHLİYE TARİHİ}}} tarihinde boş ve hasarsız olarak kiraya verene teslim edecektir.");
            },
            Feragat = "Taraflar, işbu anlaşmada belirtilen hususların yerine getirilmesi ile uyuşmazlık konusu kira ilişkisinden kaynaklanan talepleri yönünden birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
            DavaSartiCumlesi = "6325 sayılı Kanunun 18/B maddesi uyarınca kira ilişkisinden kaynaklanan uyuşmazlıklarda dava açılmadan önce arabulucuya başvurulmuş olması dava şartıdır.",
        },
        new()
        {
            Folder = "Ortaklığın Giderilmesi", Name = "Ortaklığın Giderilmesi Arabuluculuğu", Category = "Ortaklığın Giderilmesi",
            Description = "Taşınır ve taşınmazların paylaştırılması ve ortaklığın giderilmesi (6325 sayılı Kanun m.18/B).",
            DavaSartiKanun = "m.18/B",
            Role1 = "Paydaş", Role2 = "Paydaş",
            Konu = "Paylı / elbirliği mülkiyetindeki taşınmazın ortaklığının giderilmesi",
            AmountLabel = "Pay devir bedeli",
            ExtraGroups = ["Taşınmaz"],
            Fields =
            {
                ["TAŞINMAZ İL İLÇE"] = F.Metin("Taşınmaz", "Taşınmazın il / ilçesi", "Örn. Çankaya/ANKARA"),
                ["TAŞINMAZ MAHALLE"] = F.Metin("Taşınmaz", "Mahalle / köy"),
                ["ADA NO"] = F.Metin("Taşınmaz", "Ada no"),
                ["PARSEL NO"] = F.Metin("Taşınmaz", "Parsel no"),
                ["TAŞINMAZ NİTELİĞİ"] = F.Metin("Taşınmaz", "Niteliği", "Örn. arsa, mesken, tarla"),
                ["DEVREDİLEN PAY"] = F.Metin("Taşınmaz", "Devredilen pay oranı", "Örn. 1/2"),
                ["TAPU DEVİR TARİHİ"] = F.Tarih("Taşınmaz", "Tapuda devrin yapılacağı son tarih"),
            },
            Terms = b =>
            {
                b.Para("Taraflar, {{{TAŞINMAZ İL İLÇE}}} ilçesi {{{TAŞINMAZ MAHALLE}}} mahallesi {{{ADA NO}}} ada {{{PARSEL NO}}} parsel sayılı {{{TAŞINMAZ NİTELİĞİ}}} nitelikli taşınmazın paydaşlarıdır. Taraflar ortaklığın aşağıdaki şekilde giderilmesi hususunda anlaşmışlardır:");
                b.Item("–", "{{{TARAF2 ADI}}}, taşınmazdaki {{{DEVREDİLEN PAY}}} oranındaki payını aşağıda belirtilen bedel karşılığında {{{TARAF1 ADI}}}’a devredecektir.");
                b.Item("–", "Tapu devri en geç {{{TAPU DEVİR TARİHİ}}} tarihine kadar tapu müdürlüğünde yapılacak; devir masrafları kanuni yükümlülere göre karşılanacaktır.");
            },
            Feragat = "Taraflar, işbu anlaşmanın yerine getirilmesi ile taşınmazdaki ortaklığın giderilmiş sayılacağını ve bu konuda birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
        },
        new()
        {
            Folder = "Kat Mülkiyeti", Name = "Kat Mülkiyeti Uyuşmazlığı Arabuluculuğu", Category = "Kat Mülkiyeti",
            Description = "634 sayılı Kat Mülkiyeti Kanunundan kaynaklanan uyuşmazlıklar (6325 sayılı Kanun m.18/B).",
            DavaSartiKanun = "m.18/B",
            Role1 = "Başvurucu", Role2 = "Karşı Taraf", Taraf2Yetkili = "Yönetici / yetkili",
            Konu = "Ortak gider ve aidat borcundan kaynaklanan alacak talebi",
            ExtraGroups = ["Ana Taşınmaz"],
            Fields =
            {
                ["ANA TAŞINMAZ"] = F.Metin("Ana Taşınmaz", "Ana taşınmazın adı / adresi", "Örn. X Sitesi, Y Mah. Z Sk. No:1"),
                ["BAĞIMSIZ BÖLÜM"] = F.Metin("Ana Taşınmaz", "Bağımsız bölüm", "Örn. B Blok 12 numaralı daire"),
                ["BORÇ DÖNEMİ"] = F.Metin("Ana Taşınmaz", "Borcun ait olduğu dönem", "Örn. Ocak–Haziran 2026", optional: true),
                ["KARŞI TARAF YÜKÜMLÜLÜĞÜ"] = F.Metin("Ana Taşınmaz", "Ödeme dışındaki yükümlülük", "Örn. ortak alandaki eşyaları kaldırmak", optional: true),
            },
            Terms = b =>
            {
                b.Para("Uyuşmazlık, {{{ANA TAŞINMAZ}}} ana taşınmazındaki {{{BAĞIMSIZ BÖLÜM}}} bağımsız bölüme ilişkindir. Taraflar aşağıdaki hususlarda anlaşmışlardır:");
                b.Item("–", "{{{BORÇ DÖNEMİ}}} dönemine ait ortak gider ve aidat borcu aşağıdaki şekilde ödenecektir.");
                b.Item("–", "Karşı taraf ayrıca {{{KARŞI TARAF YÜKÜMLÜLÜĞÜ}}} yükümlülüğünü üstlenmiştir.");
            },
            Feragat = "Taraflar, işbu anlaşmada belirtilen hususların yerine getirilmesi ile uyuşmazlık konusu talepler yönünden birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
        },
        new()
        {
            Folder = "Komşu Hakkı", Name = "Komşu Hakkı Uyuşmazlığı Arabuluculuğu", Category = "Komşu Hakkı",
            Description = "Komşu hakkından kaynaklanan uyuşmazlıklar (6325 sayılı Kanun m.18/B).",
            DavaSartiKanun = "m.18/B",
            Role1 = "Başvurucu", Role2 = "Karşı Taraf",
            Konu = "Komşuluk hukukundan kaynaklanan müdahalenin men’i ve tazminat talebi",
            ExtraGroups = ["Komşuluk"],
            Fields =
            {
                ["TAŞINMAZ ADRESİ"] = F.Metin("Komşuluk", "Uyuşmazlığa konu taşınmaz(lar)ın adresi"),
                ["YÜKÜMLÜLÜK"] = F.Metin("Komşuluk", "Karşı tarafın üstlendiği yükümlülük", "Örn. sınırdaki ağaçları budamak, gürültüyü önleyici yalıtım yaptırmak"),
                ["YÜKÜMLÜLÜK TARİHİ"] = F.Tarih("Komşuluk", "Yükümlülüğün yerine getirileceği son tarih"),
            },
            Terms = b =>
            {
                b.Para("Uyuşmazlık, {{{TAŞINMAZ ADRESİ}}} adresindeki taşınmazlar arasındaki komşuluk ilişkisinden kaynaklanmaktadır. Taraflar aşağıdaki hususlarda anlaşmışlardır:");
                b.Item("–", "Karşı taraf, {{{YÜKÜMLÜLÜK}}} hususunu en geç {{{YÜKÜMLÜLÜK TARİHİ}}} tarihine kadar yerine getirecektir.");
            },
            Feragat = "Taraflar, işbu anlaşmada belirtilen hususların yerine getirilmesi ile uyuşmazlık konusu talepler yönünden birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
        },
        new()
        {
            Folder = "Tarımsal Üretim Sözleşmesi", Name = "Tarımsal Üretim Sözleşmesi Arabuluculuğu", Category = "Tarımsal Üretim Sözleşmesi",
            Description = "Tarımsal üretim sözleşmesinden doğan uyuşmazlıklar (5488 sayılı Tarım Kanunu m.13).",
            DavaSartiKanun = "5488 sayılı Tarım Kanunu m.13",
            Role1 = "Üretici", Role2 = "Alıcı", Taraf2Yetkili = "Alıcı yetkilisi",
            Konu = "Tarımsal üretim sözleşmesinden kaynaklanan ürün bedeli alacağı",
            Sure = "iki hafta içinde sonuçlandırır; bu süre zorunlu hâllerde arabulucu tarafından en fazla bir hafta uzatılabilir",
            ExtraGroups = ["Üretim Sözleşmesi"],
            Fields =
            {
                ["ÜRETİM SÖZLEŞMESİ TARİHİ"] = F.Tarih("Üretim Sözleşmesi", "Tarımsal üretim sözleşmesinin tarihi"),
                ["ÜRÜN"] = F.Metin("Üretim Sözleşmesi", "Ürün cinsi ve miktarı", "Örn. 40 ton yaş çay"),
                ["TESLİM TARİHİ"] = F.Tarih("Üretim Sözleşmesi", "Ürünün teslim edileceği tarih", optional: true),
            },
            Terms = b =>
            {
                b.Para("Taraflar arasında {{{ÜRETİM SÖZLEŞMESİ TARİHİ}}} tarihli tarımsal üretim sözleşmesi bulunmaktadır. Taraflar, sözleşme konusu {{{ÜRÜN}}} ürününe ilişkin uyuşmazlığın aşağıdaki şekilde çözümü hususunda anlaşmışlardır:");
                b.Item("–", "Üretici, ürünü en geç {{{TESLİM TARİHİ}}} tarihinde alıcıya teslim edecektir.");
            },
            Feragat = "Taraflar, işbu anlaşmada belirtilen hususların yerine getirilmesi ile tarımsal üretim sözleşmesinden kaynaklanan uyuşmazlık konusu talepler yönünden birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
        },
        new()
        {
            Folder = "İhtiyari Arabuluculuk", Name = "İhtiyari Arabuluculuk (Genel)", Category = "İhtiyari (Genel)",
            Description = "Tarafların kendi istekleriyle başvurduğu arabuluculuk; her türlü özel hukuk uyuşmazlığı için genel set.",
            Role1 = "Taraf", Role2 = "Taraf",
            Konu = "Taraflar arasındaki alacak uyuşmazlığı",
            ExtraGroups = [Konu],
            Fields =
            {
                ["ANLAŞMA ŞARTLARI"] = F.Metin(Konu, "Anlaşma şartları", "Ödeme dışında üzerinde anlaşılan hususlar", optional: true),
            },
            Terms = b => b.Para("{{{ANLAŞMA ŞARTLARI}}}"),
            Feragat = "Taraflar, işbu anlaşmada belirtilen hususların yerine getirilmesi ile uyuşmazlık konusu talepler yönünden birbirlerinden başkaca bir taleplerinin kalmadığını kabul ve beyan etmişlerdir.",
        },
    ];
}

/// <summary>Writes the documents and set.json of one set.</summary>
static class SetWriter
{
    const string Dosya = "Dosya", Arabulucu = "Arabulucu", Taraf1 = "Taraf-1", Taraf2 = "Taraf-2", Toplanti = "Toplantı",
        Anlasma = "Anlaşma ve Ödeme", Ucret = "Arabuluculuk Ücreti", Sonuc = "Sonuç";

    const string DocBelirleme = "01 Arabulucu Belirleme Tutanağı.docx";
    const string DocDavet = "02 İlk Toplantıya Davet.docx";
    const string DocIlk = "03 Bilgilendirme ve İlk Oturum Tutanağı.docx";
    const string DocAnlasma = "04 Anlaşma Belgesi.docx";
    const string DocSonAnlasma = "05 Son Tutanak - Anlaşma.docx";
    const string DocSonAnlasamama = "06 Son Tutanak - Anlaşamama.docx";
    const string DocSonKatilmama = "07 Son Tutanak - Toplantıya Katılmama.docx";

    public static void Write(Spec s, string folder)
    {
        Directory.CreateDirectory(folder);
        var docs = new List<DocumentEntry>
        {
            new() { Template = DocBelirleme, Optional = s.DavaSarti, Note = s.DavaSarti ? "Taraflar arabulucu üzerinde başvuru sırasında anlaştıysa" : "Arabulucu seçimi ve görevin kabulü" },
            new() { Template = DocDavet, Optional = true, Note = "Taraflara gönderilecek yazılı davet" },
            new() { Template = DocIlk, Note = "İlk toplantıda imzalanır" },
            new() { Template = DocAnlasma, Note = "Anlaşma hâlinde" },
            new() { Template = DocSonAnlasma, Note = "Anlaşma hâlinde" },
            new() { Template = DocSonAnlasamama, Optional = true, Note = "Anlaşma sağlanamazsa (anlaşma belgelerinin yerine)" },
            new() { Template = DocSonKatilmama, Optional = true, Note = "Taraflardan biri ilk toplantıya katılmazsa ya da ulaşılamazsa" },
        };

        Save(folder, DocBelirleme, Belirleme(s));
        Save(folder, DocDavet, s.OrnekMetin ? DavetMektubu(s) : Davet(s));
        Save(folder, DocIlk, s.OrnekMetin ? IlkOturumOrnek(s) : IlkOturum(s));
        Save(folder, DocAnlasma, AnlasmaBelgesi(s));
        Save(folder, DocSonAnlasma, s.OrnekMetin ? SonTutanakOrnek(s, anlasma: true) : SonTutanak(s, "ANLAŞMA", Paragraphs.SonAnlasma));
        Save(folder, DocSonAnlasamama, s.OrnekMetin ? SonTutanakOrnek(s, anlasma: false) : SonTutanak(s, "ANLAŞAMAMA", Paragraphs.SonAnlasamama));
        Save(folder, DocSonKatilmama, SonKatilmama(s));

        var def = new SetDefinition
        {
            Name = s.Name,
            Category = s.Category,
            Description = s.Description,
            Formats = new SetFormats { Date = "dd.MM.yyyy", Money = "{tutar} {simge}", Phone = "0### ### ## ##" },
            OutputFolder = "{{{TARAF1 ADI}}} - " + s.Category.Replace("–", "-"),
            Documents = docs,
            Groups = [Dosya, Arabulucu, Taraf1, Taraf2, .. s.ExtraGroups, Anlasma, Ucret, Toplanti, Sonuc],
            Fields = Fields(s),
            Checks = Checks(s),
        };
        def.Save(Path.Combine(folder, SetDefinition.FileName));
    }

    static void Save(string folder, string name, DocxBuilder b) => File.WriteAllBytes(Path.Combine(folder, name), b.Build());

    // ------------------------------------------------------------------ fields

    static Dictionary<string, FieldDefinition> Fields(Spec s)
    {
        var f = new Dictionary<string, FieldDefinition>
        {
            ["DOSYA NO"] = F.Metin(Dosya, "Arabuluculuk dosya numarası", "Örn. 2026/1234"),
        };
        if (s.DavaSarti)
        {
            f["BÜRO DOSYA NO"] = F.Metin(Dosya, "Arabuluculuk bürosu dosya numarası", "Örn. 2026/56789");
            f["ARABULUCULUK BÜROSU"] = F.Metin(Dosya, "Görevlendiren arabuluculuk bürosu", "Örn. Ankara Adliyesi Arabuluculuk Bürosu");
        }
        f["BAŞVURU TARİHİ"] = F.Tarih(Dosya, "Başvuru tarihi");
        f["GÖREVLENDİRME TARİHİ"] = F.Tarih(Dosya, s.DavaSarti ? "Arabulucunun görevlendirildiği tarih" : "Arabulucunun görevi kabul ettiği tarih", from: "BAŞVURU TARİHİ");
        f["UYUŞMAZLIK KONUSU"] = F.Metin(Dosya, "Uyuşmazlığın konusu", def: s.Konu);
        if (s.DavaSarti) f["SONUÇLANDIRMA SÜRESİ"] = F.Metin(Dosya, "Yasal süre (bilgilendirme metni)", def: s.Sure);

        f["ARABULUCU ADI SOYADI"] = F.Metin(Arabulucu, "Adı soyadı", transform: "upper", hint: "“Hatırla” ile bir kez girin");
        f["ARABULUCU SİCİL NO"] = F.Metin(Arabulucu, "Arabulucu sicil numarası", pattern: "^\\d+$");
        f["ARABULUCU TCKN"] = F.Of("TCKN", Arabulucu, "T.C. kimlik numarası");
        f["ARABULUCU ADRES"] = F.Metin(Arabulucu, "Büro adresi");
        f["ARABULUCU TELEFON"] = F.Of("Telefon", Arabulucu, "Telefon");
        f["ARABULUCU E-POSTA"] = F.Metin(Arabulucu, "E-posta", pattern: "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");
        f["ARABULUCU IBAN"] = F.Of("IBAN", Arabulucu, "Ücretin ödeneceği IBAN");

        foreach (var (n, group, role, yetkili) in new[] { (1, Taraf1, s.Role1, (string?)null), (2, Taraf2, s.Role2, s.Taraf2Yetkili) })
        {
            f[$"TARAF{n} SIFATI"] = F.Metin(group, "Sıfatı", def: role);
            f[$"TARAF{n} ADI"] = F.Metin(group, "Adı soyadı / unvanı", transform: "upper");
            f[$"TARAF{n} KİMLİK NO"] = F.Metin(group, "T.C. kimlik / vergi numarası", "Gerçek kişide 11 haneli TCKN, şirkette 10 haneli VKN", pattern: "^\\d{10,11}$");
            f[$"TARAF{n} ADRES"] = F.Metin(group, "Adresi");
            f[$"TARAF{n} TELEFON"] = F.Of("Telefon", group, "Telefon", optional: true);
            f[$"TARAF{n} YETKİLİSİ"] = F.Metin(group, yetkili ?? "Yetkilisi / temsilcisi", "Tüzel kişi ya da temsil varsa", optional: true, transform: "upper");
            f[$"TARAF{n} VEKİLİ"] = F.Metin(group, "Vekili (avukat)", "Örn. Av. Ayşe YILMAZ (Ankara Barosu)", optional: true);
        }
        foreach (var (k, v) in s.Fields) f[k] = v;

        f["ANLAŞMA TUTARI"] = s.AmountComputed is null ? F.Para(Anlasma, s.AmountLabel) : F.Para(Anlasma, s.AmountLabel, computed: s.AmountComputed);
        f["ÖDEME YAPACAK TARAF"] = F.Metin(Anlasma, "Ödemeyi yapacak taraf", "Boş bırakılırsa Taraf-2", from: "TARAF2 ADI", transform: "upper");
        f["ÖDEME ALACAK TARAF"] = F.Metin(Anlasma, "Ödemeyi alacak taraf", "Boş bırakılırsa Taraf-1", from: "TARAF1 ADI", transform: "upper");
        f["ÖDEME IBAN"] = F.Of("IBAN", Anlasma, "Ödemenin yapılacağı IBAN", optional: true);
        for (int i = 1; i <= 4; i++)
        {
            f[$"TAKSİT{i} TARİHİ"] = F.Tarih(Anlasma, i == 1 ? "1. ödeme tarihi" : $"{i}. taksit tarihi", optional: i > 1);
            f[$"TAKSİT{i} TUTARI"] = F.Para(Anlasma, i == 1 ? "1. ödeme tutarı" : $"{i}. taksit tutarı", optional: i > 1);
        }
        f["EK HUSUSLAR"] = F.Metin(Anlasma, "Ek hususlar", "Belgeye ayrı bir paragraf olarak eklenir", optional: true);

        f["TARİFE YILI"] = F.Metin(Ucret, "Arabuluculuk ücret tarifesi yılı", pattern: "^\\d{4}$", def: "2026");
        f["ARABULUCULUK ÜCRETİ"] = F.Para(Ucret, "Arabuluculuk ücreti (KDV ve stopaj dâhil)");
        f["ÜCRET PAYLAŞIMI"] = F.Metin(Ucret, "Ücretin karşılanma şekli", def: "taraflarca eşit olarak");
        f["ÜCRET ÖDEME TARİHİ"] = F.Tarih(Ucret, "Ücretin son ödeme tarihi");

        f["İLK OTURUM TARİHİ"] = F.Tarih(Toplanti, "İlk toplantı tarihi");
        f["TOPLANTI SAATİ"] = F.Metin(Toplanti, "İlk toplantı saati", "Örn. 14:00", pattern: "^\\d{1,2}[:.]\\d{2}$");
        f["TOPLANTI YERİ"] = F.Metin(Toplanti, "Toplantı yeri", "Boş bırakılırsa arabulucu adresi", from: "ARABULUCU ADRES");
        if (s.OrnekMetin)
        {
            // Meeting in person (place) or by video conference: the paragraphs of the other kind drop out.
            f["TOPLANTI YERİ"] = F.Metin(Toplanti, "Toplantı yeri (yüz yüze)",
                "Boş bırakılırsa arabulucu adresi. Telekonferansta silgiyle “boş bırak”ın.", from: "ARABULUCU ADRES");
            f["TOPLANTI YERİ"].RemoveParagraphIfBlank = true;
            f["TELEKONFERANS"] = F.Metin(Toplanti, "Telekonferans (yüz yüze ise boş)",
                "Örn. taraf vekillerinin talebi doğrultusunda telekonferans yoluyla", optional: true);
            f["TELEKONFERANS KATILIM"] = F.Metin(Toplanti, "Telekonferansta aranan hatlar",
                "Örn. başvurucu vekili Av. Ayşe YILMAZ 0 (532) 000 00 00 no’lu hattan, karşı taraf vekili Av. Ali KAYA 0 (533) 000 00 00 no’lu hattan",
                optional: true);
            f["İMZA ŞEKLİ"] = F.Metin(Toplanti, "İmza şekli", "Elektronik imzada: hep birlikte elektronik imza marifetiyle", def: "hep birlikte");
            f["E-İMZA NOTU"] = new FieldDefinition
            {
                Type = "Metin", Group = Toplanti, Label = "İmzaların altındaki not", Hint = "Elektronik imzada “boş bırak”ı kaldırın",
                Default = "e-imzalıdır!", RemoveParagraphIfBlank = true, BlankByDefault = true,
            };
        }
        f["DAVET TARİHİ"] = F.Tarih(Toplanti, "Davet yazısının tarihi", from: "GÖREVLENDİRME TARİHİ");
        f["DAVET YÖNTEMİ"] = F.Metin(Toplanti, "Davet yöntemi", def: "telefon, kısa mesaj ve e-posta");
        f["SON OTURUM TARİHİ"] = F.Tarih(Toplanti, "Son toplantı / tutanak tarihi", from: "İLK OTURUM TARİHİ");
        f["NÜSHA SAYISI"] = F.Metin(Toplanti, "Nüsha sayısı (yazıyla)", def: "üç");

        f["GÖRÜŞME SÜRESİ"] = F.Metin(Sonuc, "Görüşmelerin toplam süresi", def: "iki saat");
        f["KATILMAYAN TARAF"] = F.Metin(Sonuc, "İlk toplantıya katılmayan taraf", from: "TARAF2 ADI", transform: "upper");
        return f;
    }

    static List<CheckDefinition> Checks(Spec s)
    {
        var list = new List<CheckDefinition>
        {
            new()
            {
                Kind = "sumEquals", Fields = ["TAKSİT1 TUTARI", "TAKSİT2 TUTARI", "TAKSİT3 TUTARI", "TAKSİT4 TUTARI"], EqualsField = "ANLAŞMA TUTARI",
                Message = "Ödemelerin toplamı anlaşma tutarına eşit değil.",
            },
            new() { Kind = "dateOrder", Fields = ["BAŞVURU TARİHİ", "GÖREVLENDİRME TARİHİ", "İLK OTURUM TARİHİ", "SON OTURUM TARİHİ"], Message = "Tarihler sırasız: başvuru ≤ görevlendirme ≤ ilk oturum ≤ son oturum olmalı." },
            new() { Kind = "dateOrder", Fields = ["TAKSİT1 TARİHİ", "TAKSİT2 TARİHİ", "TAKSİT3 TARİHİ", "TAKSİT4 TARİHİ"], Message = "Ödeme tarihleri sıralı olmalı." },
        };
        for (int i = 2; i <= 4; i++)
            list.Add(new() { Kind = "blankTogether", Fields = [$"TAKSİT{i} TARİHİ", $"TAKSİT{i} TUTARI"], Message = $"{i}. taksitin tarihi ve tutarı birlikte girilmeli ya da birlikte boş bırakılmalı." });
        if (s.OrnekMetin)
            list.Add(new()
            {
                Kind = "atMostOne", Fields = ["TOPLANTI YERİ", "TELEKONFERANS"],
                Message = "Toplantı ya yüz yüze ya telekonferansla yapılır: telekonferansta “Toplantı yeri”ni silgiyle boş bırakın.",
            });
        list.AddRange(s.Checks);
        return list;
    }

    // ------------------------------------------------------------------ shared blocks

    static string Kanun(Spec s) => s.DavaSartiKanun switch
    {
        null => "6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu",
        var k when k.StartsWith("m.") => $"6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu m.18/A ve {k}",
        var k => $"6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu m.18/A ve {k}",
    };

    static DocxBuilder Head(string title, Spec s)
    {
        var b = new DocxBuilder();
        b.Title(s.DavaSarti ? "DAVA ŞARTI ARABULUCULUK" : "HUKUK UYUŞMAZLIKLARINDA ARABULUCULUK", 24);
        b.Title(title);
        b.Empty();
        return b;
    }

    static void FileInfo(DocxBuilder b, Spec s, bool basvuru = true)
    {
        if (s.DavaSarti)
        {
            b.Field("Arabuluculuk Bürosu", "{{{ARABULUCULUK BÜROSU}}}");
            b.Field("Büro Dosya Numarası", "{{{BÜRO DOSYA NO}}}");
        }
        b.Field("Arabuluculuk Dosya Numarası", "{{{DOSYA NO}}}");
        if (basvuru) b.Field("Başvuru Tarihi", "{{{BAŞVURU TARİHİ}}}");
    }

    static void ArabulucuInfo(DocxBuilder b)
    {
        b.Field("**ARABULUCU**", "");
        b.Field("Adı Soyadı", "{{{ARABULUCU ADI SOYADI}}}");
        b.Field("T.C. Kimlik Numarası", "{{{ARABULUCU TCKN}}}");
        b.Field("Sicil Numarası", "{{{ARABULUCU SİCİL NO}}}");
        b.Field("Adresi", "{{{ARABULUCU ADRES}}}");
    }

    static void Taraf(DocxBuilder b, int n)
    {
        b.Field($"**TARAF-{n}** ({{{{{{TARAF{n} SIFATI}}}}}})", "");
        b.Field("Adı Soyadı / Unvanı", $"{{{{{{TARAF{n} ADI}}}}}}");
        b.Field("T.C. Kimlik / Vergi No", $"{{{{{{TARAF{n} KİMLİK NO}}}}}}");
        b.Field("Adresi", $"{{{{{{TARAF{n} ADRES}}}}}}");
        b.Field("Telefon", $"{{{{{{TARAF{n} TELEFON}}}}}}");
        b.Field("Yetkilisi", $"{{{{{{TARAF{n} YETKİLİSİ}}}}}}");
        b.Field("Vekili", $"{{{{{{TARAF{n} VEKİLİ}}}}}}");
    }

    static void Taraflar(DocxBuilder b)
    {
        Taraf(b, 1);
        b.Empty();
        Taraf(b, 2);
        b.Empty();
    }

    /// <param name="eImza">Adds the "e-imzalıdır!" line under every signature (dropped when left blank).</param>
    static void Imzalar(DocxBuilder b, bool arabulucuOnly = false, bool eImza = false)
    {
        b.Empty();
        string[] With(params string[] lines) => eImza ? [.. lines, "{{{E-İMZA NOTU}}}"] : lines;
        var arabulucu = With("ARABULUCU", "{{{ARABULUCU ADI SOYADI}}}", "Sicil No: {{{ARABULUCU SİCİL NO}}}");
        if (arabulucuOnly) { b.Signatures(arabulucu); return; }
        b.Signatures(
            With("TARAF-1", "{{{TARAF1 ADI}}}"),
            With("TARAF-2", "{{{TARAF2 ADI}}}"),
            arabulucu);
    }

    // ------------------------------------------------------------------ documents

    static DocxBuilder Belirleme(Spec s)
    {
        var b = Head("ARABULUCU BELİRLEME TUTANAĞI", s);
        Taraflar(b);
        b.Field("Uyuşmazlığın Konusu", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Empty();
        if (s.DavaSarti)
            b.Para($"Taraflar, aralarındaki uyuşmazlığın {Kanun(s)} hükümleri çerçevesinde arabuluculuk yoluyla çözümü için, adliye arabuluculuk bürosuna başvuru sırasında arabulucu listesinde yer alan {{{{{{ARABULUCU SİCİL NO}}}}}} sicil numaralı Arabulucu {{{{{{ARABULUCU ADI SOYADI}}}}}} üzerinde anlaşmışlardır. Bu tutanak, anlaşılan arabulucunun görevlendirilmesi için arabuluculuk bürosuna sunulmak üzere düzenlenmiştir.", indent: true);
        else
            b.Para("Taraflar, aralarındaki uyuşmazlığın 6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu çerçevesinde arabuluculuk yoluyla çözümü için {{{ARABULUCU SİCİL NO}}} sicil numaralı Arabulucu {{{ARABULUCU ADI SOYADI}}}’ı arabulucu olarak seçmişler; arabulucu da {{{GÖREVLENDİRME TARİHİ}}} tarihinde görevi kabul etmiştir.", indent: true);
        b.Para("{{{GÖREVLENDİRME TARİHİ}}}", DocxAlign.Right);
        Imzalar(b);
        b.Para("Ekler: Kimlik fotokopileri; tüzel kişiler için imza sirküleri ve yetki belgesi; vekil için vekâletname.", DocxAlign.Left);
        return b;
    }

    static DocxBuilder Davet(Spec s)
    {
        var b = new DocxBuilder();
        b.Title("İLK TOPLANTIYA DAVET");
        b.Empty();
        b.Para("{{{DAVET TARİHİ}}}", DocxAlign.Right);
        FileInfo(b, s, basvuru: false);
        b.Empty();
        b.Para("Sayın {{{TARAF1 ADI}}}", DocxAlign.Left, after: 0);
        b.Para("Sayın {{{TARAF2 ADI}}}", DocxAlign.Left);
        if (s.DavaSarti)
            b.Para("Aranızdaki {{{UYUŞMAZLIK KONUSU}}} konulu uyuşmazlıkta {{{ARABULUCULUK BÜROSU}}} tarafından {{{GÖREVLENDİRME TARİHİ}}} tarihinde arabulucu olarak görevlendirildim.", indent: true);
        else
            b.Para("Aranızdaki {{{UYUŞMAZLIK KONUSU}}} konulu uyuşmazlıkta {{{GÖREVLENDİRME TARİHİ}}} tarihinde arabulucu olarak görevi kabul ettim.", indent: true);
        b.Para("Uyuşmazlığın arabuluculuk yoluyla görüşülmesi amacıyla yapılacak ilk toplantı **{{{İLK OTURUM TARİHİ}}} tarihinde, saat {{{TOPLANTI SAATİ}}} itibarıyla {{{TOPLANTI YERİ}}}** adresinde yapılacaktır. Toplantıya kimlik belgenizle katılmanızı; vekil ya da yetkili aracılığıyla katılacaksanız vekâletname veya yetki belgesini yanınızda getirmenizi rica ederim.", indent: true);
        if (s.DavaSarti)
            b.Para("Geçerli bir mazeret göstermeksizin ilk toplantıya katılmayan tarafın, arabuluculuk faaliyetinin bu nedenle sona ermesi hâlinde açılacak davada kısmen veya tamamen haklı çıksa bile yargılama giderlerinin tamamından sorumlu tutulacağını ve lehine vekâlet ücretine hükmedilmeyeceğini (6325 sayılı Kanun m.18/A) hatırlatırım.", indent: true);
        b.Para("Toplantı tarihi veya yeri konusunda bir talebiniz varsa aşağıdaki iletişim bilgilerinden bana ulaşabilirsiniz.", indent: true);
        b.Para("Saygılarımla,", DocxAlign.Left);
        b.Empty();
        b.Signatures(["ARABULUCU", "{{{ARABULUCU ADI SOYADI}}}", "Sicil No: {{{ARABULUCU SİCİL NO}}}", "Tel: {{{ARABULUCU TELEFON}}}", "{{{ARABULUCU E-POSTA}}}"]);
        return b;
    }

    static DocxBuilder IlkOturum(Spec s)
    {
        var b = Head("BİLGİLENDİRME VE İLK OTURUM TUTANAĞI", s);
        FileInfo(b, s);
        b.Empty();
        ArabulucuInfo(b);
        b.Empty();
        Taraflar(b);
        b.Field("Uyuşmazlığın Konusu", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Field("Toplantı Yeri", "{{{TOPLANTI YERİ}}}");
        b.Empty();
        b.Para($"Yukarıda bilgileri yazılı taraflar, {{{{{{İLK OTURUM TARİHİ}}}}}} tarihinde {Kanun(s)} uyarınca yürütülen arabuluculuk faaliyeti kapsamında ilk oturum için bir araya gelmiştir. Hazır bulunanların kimlik ve varsa temsil / vekâlet belgeleri kontrol edilmiştir.", indent: true);
        foreach (var p in Paragraphs.IlkOturum(s)) b.Para(p, indent: true);
        b.Para("İşbu bilgilendirme ve ilk oturum tutanağı, hazır bulunanlar tarafından okunarak {{{İLK OTURUM TARİHİ}}} tarihinde birlikte imza altına alınmıştır.", indent: true);
        Imzalar(b);
        return b;
    }

    static DocxBuilder AnlasmaBelgesi(Spec s)
    {
        var b = Head("ANLAŞMA BELGESİ", s);
        FileInfo(b, s);
        b.Empty();
        ArabulucuInfo(b);
        b.Empty();
        Taraflar(b);
        b.Field("Uyuşmazlığın Konusu", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Field("Faaliyetin Başladığı Tarih", "{{{GÖREVLENDİRME TARİHİ}}}");
        b.Field("Faaliyetin Sona Erdiği Tarih", "{{{SON OTURUM TARİHİ}}}");
        b.Field("Belgenin Düzenlendiği Yer", "{{{TOPLANTI YERİ}}}");
        b.Field("Arabuluculuk Sonucu", "**ANLAŞMA**");
        b.Empty();
        b.Para("Hazır bulunan taraflara arabuluculuğun temel ilkeleri, kişisel verilerin korunması, arabuluculuk süreci ve süreç sonunda düzenlenen anlaşma belgesinin hukuki ve mali sonuçları hakkında bilgi verilmiştir. Taraflar bu hususları anladıklarını beyan etmişlerdir.", indent: true);
        b.Para("Taraflar arabuluculuk sürecinde {{{UYUŞMAZLIK KONUSU}}} hususlarını müzakere etmiş ve aşağıdaki şartlarla **ANLAŞMAYA VARMIŞLARDIR:**", indent: true);
        s.Terms(b);
        b.Para("**Ödeme:** Mutabık kalınan toplam {{{ANLAŞMA TUTARI}}} ({{{ANLAŞMA TUTARI.Yazıyla}}}), {{{ÖDEME YAPACAK TARAF}}} tarafından {{{ÖDEME ALACAK TARAF}}}’a aşağıdaki tarihlerde ödenecektir:", indent: true);
        for (int i = 1; i <= 4; i++)
            b.Item("–", $"{{{{{{TAKSİT{i} TARİHİ}}}}}} tarihinde {{{{{{TAKSİT{i} TUTARI}}}}}} ({{{{{{TAKSİT{i} TUTARI.Yazıyla}}}}}})");
        b.Para("Ödemeler {{{ÖDEME ALACAK TARAF}}}’ın {{{ÖDEME IBAN}}} IBAN numaralı banka hesabına yapılacaktır.", indent: true);
        b.Para("Ödemelerden herhangi birinin vadesinde yapılmaması hâlinde kalan borcun tamamı muaccel hâle gelecektir.", indent: true);
        b.Para(s.Feragat, indent: true);
        b.Para("{{{EK HUSUSLAR}}}", indent: true);
        b.Para("**Arabuluculuk ücreti:** Yürürlükteki {{{TARİFE YILI}}} yılı Arabuluculuk Asgari Ücret Tarifesi uyarınca belirlenen {{{ARABULUCULUK ÜCRETİ}}} ({{{ARABULUCULUK ÜCRETİ.Yazıyla}}}) arabuluculuk ücreti {{{ÜCRET PAYLAŞIMI}}} karşılanacak ve {{{ÜCRET ÖDEME TARİHİ}}} tarihine kadar, arabulucunun düzenleyeceği serbest meslek makbuzu karşılığında Arabulucu {{{ARABULUCU ADI SOYADI}}}’ın {{{ARABULUCU IBAN}}} IBAN numaralı hesabına ödenecektir.", indent: true);
        b.Para("Taraflar, işbu anlaşma belgesinde yer alan hususlar dışında uyuşmazlık konusu hususlarda birbirlerinden başkaca bir talepleri olmadığını ve üzerinde anlaşılan hususlarda dava açılamayacağını bildiklerini kabul ve beyan etmişlerdir. Taraflar ve avukatları ile arabulucunun birlikte imzaladıkları anlaşma belgesi, 6325 sayılı Kanun m.18 uyarınca icra edilebilirlik şerhi aranmaksızın ilam niteliğinde belge sayılır; diğer hâllerde taraflar icra edilebilirlik şerhi verilmesini talep edebilirler.", indent: true);
        b.Para("İşbu anlaşma belgesi tarafların serbest iradeleriyle {{{NÜSHA SAYISI}}} nüsha olarak düzenlenmiş, birlikte imza altına alınmış ve birer nüshası taraflara verilmiştir. {{{SON OTURUM TARİHİ}}}", indent: true);
        Imzalar(b, eImza: s.OrnekMetin);
        return b;
    }

    static DocxBuilder SonTutanak(Spec s, string sonuc, Func<Spec, IEnumerable<string>> body)
    {
        var b = Head("SON TUTANAK", s);
        FileInfo(b, s);
        b.Empty();
        ArabulucuInfo(b);
        b.Empty();
        Taraflar(b);
        b.Field("Uyuşmazlığın Konusu", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Field("Faaliyetin Başladığı Tarih", "{{{GÖREVLENDİRME TARİHİ}}}");
        b.Field("Faaliyetin Sona Erdiği Tarih", "{{{SON OTURUM TARİHİ}}}");
        b.Field("Tutanağın Düzenlendiği Yer", "{{{TOPLANTI YERİ}}}");
        b.Field("Arabuluculuk Sonucu", $"**{sonuc}**");
        b.Empty();
        b.Para($"Yukarıda bilgileri yazılı taraflar, {{{{{{SON OTURUM TARİHİ}}}}}} tarihinde {Kanun(s)} uyarınca yürütülen arabuluculuk faaliyeti kapsamında bir araya gelmiştir. Tarafların kimlik kontrolleri yapılmıştır.", indent: true);
        foreach (var p in body(s)) b.Para(p, indent: true);
        b.Para($"İşbu son tutanak {{{{{{NÜSHA SAYISI}}}}}} nüsha olarak {Kanun(s)} uyarınca düzenlenmiş ve hazır bulunanlarca birlikte imza altına alınmıştır. {{{{{{SON OTURUM TARİHİ}}}}}}", indent: true);
        Imzalar(b);
        return b;
    }

    // ------------------------------------------------------------------ documents in the form of the sample files

    /// <summary>Header of the first session and final records: office, mediator, parties, dates.</summary>
    static DocxBuilder OrnekBaslik(Spec s, string title, string tarih)
    {
        var b = new DocxBuilder();
        b.Title("HUKUK UYUŞMAZLIKLARINDA DAVA ŞARTI ARABULUCULUK", 24);
        b.Title(title);
        b.Empty();
        b.Field("Arabuluculuk Bürosu", "{{{ARABULUCULUK BÜROSU}}}");
        b.Field("Dosya Numarası", "{{{DOSYA NO}}}");
        b.Field("Başvuru Dosya Numarası", "{{{BÜRO DOSYA NO}}}");
        b.Empty();
        b.Field("**ARABULUCU**", "{{{ARABULUCU ADI SOYADI}}}");
        b.Field("T.C. Kimlik Numarası", "{{{ARABULUCU TCKN}}}");
        b.Field("Arabulucu Sicil Numarası", "{{{ARABULUCU SİCİL NO}}}");
        b.Field("Adresi", "{{{ARABULUCU ADRES}}}");
        b.Field("Cep Telefonu", "{{{ARABULUCU TELEFON}}}");
        b.Empty();
        Taraflar(b);
        b.Field("Arabuluculuk Konusu Uyuşmazlık", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Field("Arabuluculuk Bürosuna Başvuru Tarihi", "{{{BAŞVURU TARİHİ}}}");
        b.Field("Arabulucunun Görevlendirildiği Tarih", "{{{GÖREVLENDİRME TARİHİ}}}");
        b.Field(title.Contains("SON") ? "Son Tutanağın Düzenlendiği Tarih" : "Tutanağın Düzenlendiği Tarih", $"{{{{{{{tarih}}}}}}}");
        return b;
    }

    /// <summary>How the parties met: in person (meeting place) or by video conference; the other paragraph drops out.</summary>
    static void ToplantiParagrafi(DocxBuilder b, string tarih)
    {
        b.Para($"Adı geçen taraflar {{{{{{{tarih}}}}}}} tarihinde 6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu gereğince yürütülen arabuluculuk faaliyeti için {{{{{{TOPLANTI YERİ}}}}}} adresindeki toplantıya geldiler. Tarafların kimlik kontrolleri yapılmıştır.", indent: true);
        b.Para($"Taraflara davet yazıları gönderildi. Toplantının {{{{{{TELEKONFERANS}}}}}} yapılması kararlaştırıldı. {{{{{{{tarih}}}}}}} tarihinde saat {{{{{{TOPLANTI SAATİ}}}}}} itibarıyla {{{{{{TELEKONFERANS KATILIM}}}}}} arandı ve taraflar telekonferans görüşmeye alındı.", indent: true);
    }

    static DocxBuilder DavetMektubu(Spec s)
    {
        var b = new DocxBuilder();
        b.Para("ARABULUCU", DocxAlign.Center, after: 0);
        b.Para("**{{{ARABULUCU ADI SOYADI}}}**", DocxAlign.Center, after: 0);
        b.Para("{{{ARABULUCU TELEFON}}}  /  {{{ARABULUCU E-POSTA}}}", DocxAlign.Center);
        b.Empty();
        b.Title(s.DavetBasligi, 24);
        b.Title("DAVET MEKTUBU");
        b.Para("{{{DAVET TARİHİ}}}", DocxAlign.Right);
        b.Field("Dosya Numarası", "{{{DOSYA NO}}}");
        b.Field("Başvuru Dosya Numarası", "{{{BÜRO DOSYA NO}}}");
        b.Empty();
        b.Field("**TOPLANTI TARİHİ**", "{{{İLK OTURUM TARİHİ}}} Saat: {{{TOPLANTI SAATİ}}}");
        b.Field("**TOPLANTI YERİ**", "{{{TOPLANTI YERİ}}}");
        b.Empty();
        b.Field("**BAŞVURUCU**", "{{{TARAF1 ADI}}}");
        b.Field("Vekili", "{{{TARAF1 VEKİLİ}}}");
        b.Field("Adresi", "{{{TARAF1 ADRES}}}");
        b.Empty();
        b.Field("**KARŞI TARAF**", "{{{TARAF2 ADI}}}");
        b.Field("Vekili", "{{{TARAF2 VEKİLİ}}}");
        b.Field("Adresi", "{{{TARAF2 ADRES}}}");
        b.Empty();
        b.Para("Sayın {{{TARAF1 ADI}}} ve Sayın {{{TARAF2 ADI}}};", DocxAlign.Left);
        b.Para("“{{{UYUŞMAZLIK KONUSU}}}” talepli uyuşmazlık kapsamında {{{ARABULUCULUK BÜROSU}}} nezdinde yapılan başvuru üzerine UYAP Arabulucu Portal tarafından görevlendirilmiş, T.C. Adalet Bakanlığı’ndaki resmî sicile kayıtlı {{{ARABULUCU SİCİL NO}}} sicil numaralı arabulucuyum.", indent: true);
        b.Para("Taraflar arasındaki uyuşmazlığı dostane çözüm yolu olarak arabuluculuk ile çözmek için işbu davet mektubu tarafınıza gönderilmektedir. Talebe ilişkin hukuki uyuşmazlığın 6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu kapsamında tarafların üzerinde serbestçe tasarruf edebileceği iş ve işlemlerden doğan özel hukuk uyuşmazlığı olduğu anlaşılmaktadır.", indent: true);
        b.Para($"{s.DavaSartiCumlesi} Arabuluculuk bürosuna başvurulmasından son tutanağın düzenlendiği tarihe kadar geçen sürede zamanaşımı durur ve hak düşürücü süre işlemez.", indent: true);
        foreach (var p in s.DavetEk) b.Para(p, indent: true);
        b.Para("Davacı, arabuluculuk faaliyeti sonunda anlaşmaya varılamadığına ilişkin son tutanağın aslını veya arabulucu tarafından onaylanmış bir örneğini dava dilekçesine eklemek zorundadır.", indent: true);
        b.Para("Arabulucu, yapılan başvuruyu görevlendirildiği tarihten itibaren {{{SONUÇLANDIRMA SÜRESİ}}}. Arabulucu, taraflara ulaşılamaması, taraflar katılmadığı için görüşme yapılamaması yahut yapılan görüşmeler sonucunda anlaşmaya varılması veya varılamaması hâllerinde arabuluculuk faaliyetini sona erdirir ve son tutanağı düzenleyerek durumu derhâl arabuluculuk bürosuna bildirir.", indent: true);
        b.Para("Tarafların arabuluculuk faaliyeti sonunda anlaşmaları hâlinde, arabuluculuk ücreti, Arabuluculuk Asgari Ücret Tarifesinin eki Arabuluculuk Ücret Tarifesinin İkinci Kısmına göre aksi kararlaştırılmadıkça taraflarca eşit şekilde karşılanır. Bu durumda ücret, Tarifenin Birinci Kısmında belirlenen iki saatlik ücret tutarından az olamaz." +
               (s.UcretEk is null ? "" : " " + s.UcretEk), indent: true);
        b.Para("Arabuluculuk faaliyeti sonunda taraflara ulaşılamaması, taraflar katılmadığı için görüşme yapılamaması veya iki saatten az süren görüşmeler sonunda tarafların anlaşamamaları hâllerinde, iki saatlik ücret tutarı Tarifenin Birinci Kısmına göre Adalet Bakanlığı bütçesinden ödenir. İki saatten fazla süren görüşmeler sonunda tarafların anlaşamamaları hâlinde ise iki saati aşan kısma ilişkin ücret aksi kararlaştırılmadıkça taraflarca eşit şekilde Tarifenin Birinci Kısmına göre karşılanır. Adalet Bakanlığı bütçesinden ödenen ve taraflarca karşılanan arabuluculuk ücreti, yargılama giderlerinden sayılır.", indent: true);
        b.Para("Arabuluculuk görüşmelerine taraflar bizzat, kanuni temsilcileri veya avukatları aracılığıyla katılabilirler. Uyuşmazlığın çözümüne katkı sağlayabilecek uzman kişiler de müzakerelerde hazır bulundurulabilir." +
               (s.TemsilEk is null ? "" : " " + s.TemsilEk), indent: true);
        b.Para("Sizlerle yapacağımız ilk toplantı, {{{İLK OTURUM TARİHİ}}} tarihinde, saat {{{TOPLANTI SAATİ}}} itibarıyla {{{TOPLANTI YERİ}}} adresinde gerçekleşecektir.", indent: true);
        b.Para("Sizlerle yapacağımız ilk toplantı, {{{İLK OTURUM TARİHİ}}} tarihinde, saat {{{TOPLANTI SAATİ}}} itibarıyla {{{TELEKONFERANS}}} gerçekleşecektir.", indent: true);
        b.Para("Taraflardan birinin geçerli bir mazeret göstermeksizin ilk toplantıya katılmaması sebebiyle arabuluculuk faaliyetinin sona ermesi durumunda toplantıya katılmayan taraf, son tutanakta belirtilir ve bu taraf davada kısmen veya tamamen haklı çıksa bile yargılama giderinin tamamından sorumlu tutulur. Ayrıca bu taraf lehine vekâlet ücretine hükmedilmez. Her iki tarafın da ilk toplantıya katılmaması sebebiyle sona eren arabuluculuk faaliyeti üzerine açılacak davalarda tarafların yaptıkları yargılama giderleri kendi üzerlerinde bırakılır." +
               (s.KatilmamaEk is null ? "" : " " + s.KatilmamaEk), indent: true);
        b.Para("Toplantı gününde görüşmek dileğiyle, saygılarımla.", indent: true);
        b.Empty();
        b.Signatures(["ARABULUCU", "{{{ARABULUCU ADI SOYADI}}}", "Sicil No: {{{ARABULUCU SİCİL NO}}}", "{{{E-İMZA NOTU}}}"]);
        b.Empty();
        b.Para("Adres: {{{ARABULUCU ADRES}}}", DocxAlign.Left, after: 0);
        b.Para("Telefon: {{{ARABULUCU TELEFON}}}  ·  E-posta: {{{ARABULUCU E-POSTA}}}", DocxAlign.Left);
        return b;
    }

    static DocxBuilder IlkOturumOrnek(Spec s)
    {
        var b = OrnekBaslik(s, "BİLGİLENDİRME VE İLK OTURUM TUTANAĞI", "İLK OTURUM TARİHİ");
        b.Empty();
        ToplantiParagrafi(b, "İLK OTURUM TARİHİ");
        foreach (var p in Paragraphs.IlkOturumOrnek()) b.Para(p, indent: true);
        b.Para("İşbu arabuluculuk bilgilendirme ve ilk oturum tutanağı 6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu m. 11 ve m. 15 uyarınca hazırlanarak hazır bulunanlar tarafından {{{İMZA ŞEKLİ}}} imza altına alınmıştır. {{{İLK OTURUM TARİHİ}}}", indent: true);
        Imzalar(b, eImza: true);
        return b;
    }

    static DocxBuilder SonTutanakOrnek(Spec s, bool anlasma)
    {
        var b = OrnekBaslik(s, "SON TUTANAĞI", "SON OTURUM TARİHİ");
        b.Field("Arabuluculuk Sonucu", anlasma ? "**ANLAŞMA**" : "**ANLAŞAMAMA**");
        b.Empty();
        ToplantiParagrafi(b, "SON OTURUM TARİHİ");
        b.Para("Taraflara arabuluculuğun temel ilkeleri, arabuluculuk süreci ve arabuluculuk süreci sonunda hazırlanan arabuluculuk son tutanağının" +
               (anlasma ? " ve anlaşma belgesinin" : "") + " hukuki ve mali yönlerden bütün sonuçları hakkında bilgi verildi. Taraflar söz alarak bu hususları anladıklarını beyan ettiler.", indent: true);
        if (anlasma)
            b.Para("Taraflar arabuluculuk sürecinde yürütülen müzakereler sonucunda {{{UYUŞMAZLIK KONUSU}}} hususlarında **ANLAŞMAYA VARMIŞLARDIR.** Taraflar anlaşma şartlarını son tutanağa değil yalnızca arabuluculuk anlaşma belgesine yazmaya karar vermişlerdir. Taraflar, üzerinde anlaşılan hususlarda dava açılamayacağını anladıklarını kabul ve beyan ederek son tutanağa bu şekilde geçmesini istemişlerdir.", indent: true);
        else
        {
            b.Para("Talep konusu olan {{{UYUŞMAZLIK KONUSU}}} müzakere edilmiş olup tüm teklif, karşı teklif ve son çözüm önerilerine rağmen **ANLAŞMAYA VARILAMAMIŞTIR.**", indent: true);
            b.Para("Görüşmeler toplam {{{GÖRÜŞME SÜRESİ}}} sürmüştür.", indent: true);
        }
        var kanun = "6325 sayılı Hukuk Uyuşmazlıklarında Arabuluculuk Kanunu m. 17" + (anlasma ? " ve m. 18/A" : "") +
                    (s.SonTutanakEkKanun is null ? "" : " ile " + s.SonTutanakEkKanun);
        b.Para($"İşbu arabuluculuk son tutanağı {kanun} uyarınca hazırlanarak hazır bulunanlar tarafından {{{{{{İMZA ŞEKLİ}}}}}} imza altına alınmıştır. {{{{{{SON OTURUM TARİHİ}}}}}}", indent: true);
        Imzalar(b, eImza: true);
        return b;
    }

    static DocxBuilder SonKatilmama(Spec s)
    {
        var b = Head("SON TUTANAK", s);
        FileInfo(b, s);
        b.Empty();
        ArabulucuInfo(b);
        b.Empty();
        Taraflar(b);
        b.Field("Uyuşmazlığın Konusu", "{{{UYUŞMAZLIK KONUSU}}}");
        b.Field("Faaliyetin Başladığı Tarih", "{{{GÖREVLENDİRME TARİHİ}}}");
        b.Field("Faaliyetin Sona Erdiği Tarih", "{{{SON OTURUM TARİHİ}}}");
        b.Field("Tutanağın Düzenlendiği Yer", "{{{TOPLANTI YERİ}}}");
        b.Field("Arabuluculuk Sonucu", "**ANLAŞAMAMA (İLK TOPLANTIYA KATILMAMA)**");
        b.Empty();
        b.Para("Arabulucu, görevlendirilmesinin ardından taraflara {{{DAVET YÖNTEMİ}}} yoluyla ulaşmaya çalışmış; tarafları {{{İLK OTURUM TARİHİ}}} tarihinde, saat {{{TOPLANTI SAATİ}}} itibarıyla {{{TOPLANTI YERİ}}} adresinde yapılacak ilk toplantıya davet etmiş ve davete ilişkin işlemleri belgelendirmiştir.", indent: true);
        if (s.OrnekMetin)
            b.Para("Arabulucu, görevlendirilmesinin ardından taraflara {{{DAVET YÖNTEMİ}}} yoluyla ulaşmaya çalışmış; tarafları {{{İLK OTURUM TARİHİ}}} tarihinde, saat {{{TOPLANTI SAATİ}}} itibarıyla {{{TELEKONFERANS}}} yapılacak ilk toplantıya davet etmiş ve davete ilişkin işlemleri belgelendirmiştir.", indent: true);
        b.Para("{{{KATILMAYAN TARAF}}}, usulüne uygun davete rağmen geçerli bir mazeret bildirmeksizin ilk toplantıya katılmamıştır. Bu nedenle arabuluculuk faaliyeti anlaşmaya varılamadan sona ermiştir.", indent: true);
        if (s.DavaSarti)
            b.Para("6325 sayılı Kanun m.18/A uyarınca, geçerli bir mazeret göstermeksizin ilk toplantıya katılmaması sebebiyle arabuluculuk faaliyetinin sona ermesine yol açan taraf, açılacak davada kısmen veya tamamen haklı çıksa bile yargılama giderlerinin tamamından sorumlu tutulur ve lehine vekâlet ücretine hükmedilmez.", indent: true);
        b.Para("Tutanak, toplantıya katılmayan taraf hazır bulunmadığından bu sebep belirtilerek arabulucu ve hazır bulunan tarafça imzalanmıştır. {{{SON OTURUM TARİHİ}}}", indent: true);
        b.Empty();
        b.Signatures(["HAZIR BULUNAN TARAF", ""], ["ARABULUCU", "{{{ARABULUCU ADI SOYADI}}}", "Sicil No: {{{ARABULUCU SİCİL NO}}}"]);
        return b;
    }
}

/// <summary>Longer standard texts.</summary>
static class Paragraphs
{
    public static IEnumerable<string> IlkOturum(Spec s)
    {
        yield return "Taraflara arabuluculuğun temel ilkeleri hakkında bilgi verilmiştir: Arabuluculuk sürecinin iradi olduğu; taraflardan her birinin eşit haklara sahip olduğu; arabulucunun, taraflarca aksi kararlaştırılmadıkça arabuluculuk faaliyeti çerçevesinde kendisine sunulan veya diğer şekilde elde ettiği bilgi, belge ve kayıtları gizli tutmakla yükümlü olduğu, tarafların ve görüşmelere katılan diğer kişilerin de bu gizliliğe uymak zorunda olduğu; uyuşmazlıkla ilgili dava açıldığında yahut tahkime başvurulduğunda, arabuluculuğa katılma isteği, süreçte ileri sürülen görüş ve teklifler, herhangi bir vakıa veya iddianın kabulü ile yalnızca arabuluculuk faaliyeti dolayısıyla hazırlanan belgelerin delil olarak ileri sürülemeyeceği ve bunlar hakkında tanıklık yapılamayacağı anlatılmıştır.";
        yield return "Arabulucunun görevini özenle, tarafsız biçimde ve bizzat yerine getireceği; taraflar arasında eşitliği gözeteceği; hâkim veya hakem olmadığı, kimin haklı olduğuna karar vermeyeceği ve taraflara hukuki tavsiyede bulunamayacağı; tarafların çözüm üretemedikleri hâllerde bir çözüm önerisinde bulunabileceği ancak tarafları bu öneriyi kabule zorlayamayacağı; taraflarla ayrı ayrı veya birlikte görüşebileceği ve uyuşmazlıkla ilgili açılacak bir davada taraflardan birinin avukatı olarak görev üstlenemeyeceği açıklanmıştır. Görüşmelere tarafların bizzat, kanuni temsilcileri veya avukatları aracılığıyla katılabilecekleri belirtilmiştir.";
        yield return "Arabuluculuk faaliyeti sonunda anlaşmaya varılması hâlinde anlaşmanın kapsamının taraflarca belirleneceği; anlaşma belgesinin taraflar ve arabulucu tarafından imzalanacağı; taraflar ve avukatları ile arabulucunun birlikte imzaladıkları anlaşma belgesinin icra edilebilirlik şerhi aranmaksızın ilam niteliğinde belge sayılacağı, diğer hâllerde icra edilebilirlik şerhi talep edilebileceği; üzerinde anlaşılan hususlarda dava açılamayacağı; son tutanağa faaliyetin sonucu dışında hangi hususların yazılacağına tarafların karar vereceği anlatılmıştır.";
        if (s.DavaSarti)
        {
            yield return "Taraflara, arabuluculuk bürosuna başvurulmasından son tutanağın düzenlendiği tarihe kadar geçen sürede zamanaşımının duracağı ve hak düşürücü sürelerin işlemeyeceği; arabulucunun başvuruyu görevlendirildiği tarihten itibaren {{{SONUÇLANDIRMA SÜRESİ}}}; anlaşmaya varılamaması hâlinde yargı yoluna başvurma haklarının saklı olduğu bildirilmiştir.";
            yield return "Taraflara, anlaşmaları hâlinde arabuluculuk ücretinin Arabuluculuk Asgari Ücret Tarifesinin İkinci Kısmına göre ve aksi kararlaştırılmadıkça taraflarca eşit şekilde karşılanacağı, bu ücretin Tarifenin Birinci Kısmında belirlenen iki saatlik ücretten az olamayacağı; anlaşamamaları hâlinde iki saatlik ücretin Adalet Bakanlığı bütçesinden ödeneceği, iki saati aşan kısmın ise aksi kararlaştırılmadıkça taraflarca eşit şekilde karşılanacağı ve bu ücretlerin yargılama giderlerinden sayılacağı açıklanmıştır.";
        }
        else
        {
            yield return "Taraflara, arabuluculuk ücretinin ve giderlerinin aksi kararlaştırılmadıkça taraflarca eşit olarak karşılanacağı, ücretin Arabuluculuk Asgari Ücret Tarifesine göre belirleneceği ve tarafların diledikleri zaman süreçten çekilebilecekleri açıklanmıştır.";
        }
        yield return "6698 sayılı Kişisel Verilerin Korunması Kanunu m.10 uyarınca kişisel verilerin 6325 sayılı Kanun çerçevesinde yürütülen arabuluculuk sürecinin yürütülmesi amacıyla işleneceği; başvurucu ve karşı tarafça temin edilen ve arabuluculuk bürosunca sağlanan kişisel verilerin gerektiğinde diğer taraflarla paylaşılabileceği; süreç tutanaklarının Arabuluculuk Bilgi Sistemine yükleneceği ve arabulucu tarafından beş yıl süreyle saklanacağı; icra edilebilirlik şerhi veya icra takibi için yargı mercilerine sunulabileceği; ilgili kişilerin Kanunun 11. maddesinde sayılan haklara sahip oldukları bildirilmiştir.";
        yield return "Taraflardan, görüşmeler boyunca birbirlerine “siz” diye hitap etmeleri, söz verildiğinde sırayla ve sözleri kesilmeden konuşmaları, söz veya hareketle diğer tarafı küçük düşürmemeleri beklendiği; süreçte açık ve dürüst olunmasının ve iş birliği içinde hareket edilmesinin önemi vurgulanmıştır.";
        yield return "Taraflar, arabuluculuğun temel ilkelerini, süreci ve süreç sonunda düzenlenecek son tutanak ile anlaşma belgesinin hukuki ve mali sonuçlarını anladıklarını, bu tutanakta belirtilen kurallara uymayı kabul ettiklerini beyan etmiş ve müzakerelere başlanmıştır.";
    }

    /// <summary>Information given at the first session, as in the sample records (the same for every kind).</summary>
    public static IEnumerable<string> IlkOturumOrnek()
    {
        yield return "Taraflara arabuluculuğun temel ilkeleri olan, arabuluculuk sürecinin iradi olduğu; arabuluculuk sürecinde her iki tarafın da eşit haklara sahip olduğu; taraflarca aksi kararlaştırılmadıkça arabulucunun arabuluculuk faaliyeti çerçevesinde kendisine sunulan veya diğer bir şekilde elde ettiği bilgi ve belgeler ile diğer kayıtları gizli tutmakla yükümlü olduğu ve tarafların ve görüşmelere katılan diğer kişilerin de bu konudaki gizliliğe uymak zorunda olduğu; tarafların, arabulucunun veya arabuluculuğa katılanlar da dâhil üçüncü bir kişinin, uyuşmazlıkla ilgili hukuk davası açıldığında yahut tahkim yoluna başvurulduğunda, tarafların arabuluculuk sürecine katılma isteğini, arabuluculuk sürecinde taraflarca ileri sürülen görüşleri, önerileri ya da herhangi bir vakıanın veya iddianın kabulünü ve sadece arabuluculuk faaliyeti dolayısıyla hazırlanan belgeleri delil olarak ileri süremeyeceği ve bunlar hakkında tanıklık yapamayacağı hususları hakkında bilgi verildi.";
        yield return "Taraflara arabulucunun görevini özenle, tarafsız bir biçimde ve şahsen yerine getireceği, arabulucunun taraflar arasında eşitliği gözetmekle yükümlü olduğu, arabuluculuk müzakerelerine tarafların bizzat, kanuni temsilcileri veya vekâletnamesinde özel yetki bulunan avukatları aracılığıyla katılabileceği, arabuluculuk sürecinde arabulucunun rolünün hâkim veya hakem olmadığı, kimin haklı ya da haksız olduğu konusunda karar vermeyeceği, yargısal bir yetkinin kullanımı olarak sadece hâkim tarafından yapılabilecek işlemleri yapamayacağı, taraflara hukuki tavsiyelerde bulunamayacağı, tarafların çözüm üretemediklerinin ortaya çıkması hâlinde arabulucunun bir çözüm önerisinde bulunabileceği, yaşanılan uyuşmazlık ile ilgili çözüm seçeneklerini üreterek bir anlaşmaya ulaşabilmelerinde taraflara yardımcı olacak iletişim ortamını sağlayacağı, taraflarla ayrı ayrı veya birlikte görüşebileceği ve iletişim kurabileceği, arabulucu olarak tarafsız bir konumda olduğu, arabuluculuk sürecinin sonunda her iki tarafın da kabul edeceği bir anlaşmaya varılamaması hâlinde açılabilecek olası bir davada daha sonra avukat olarak görev üstlenemeyeceği, arabuluculuk bürosuna başvurulmasından son tutanağın düzenlendiği tarihe kadar geçen sürede zamanaşımının duracağı ve hak düşürücü sürenin işlemeyeceği, arabuluculuk sürecinin sonunda her iki tarafın da kabul edeceği bir anlaşmaya varılamaması hâlinde yargı organlarına başvuru haklarının bulunduğu hususları hakkında bilgi verildi.";
        yield return "Taraflara arabuluculuk sürecinde düzenlenecek oturum tutanaklarına ve sürecin sonunda düzenlenecek son tutanağa, oturumların ve faaliyetin sonuçlanması ile arabulucunun son çözüm önerisi dışında hangi hususların yazılacağına tarafların karar vereceği; bununla beraber, bu hususta tarafların birlikte karar verememesi hâlinde son tutanağın içeriğinin tarafların karşılıklı teklif ve kabulleri dâhil olmak üzere arabulucu tarafından düzenleneceği, arabuluculuk sürecinin sonunda varılan anlaşmanın kapsamının taraflarca belirleneceği, anlaşma belgesi düzenlenmesi hâlinde bu belgenin taraflar veya avukatları ve arabulucu tarafından imzalanacağı, tarafların bu anlaşma belgesinin icra edilebilirliğine ilişkin mahkemeden şerh verilmesini talep edebileceği ve bu şerhi içeren anlaşmanın ilâm niteliğinde belge sayılacağı, taraflar ve avukatları ile arabulucunun birlikte imzaladıkları anlaşma belgesinin icra edilebilirlik şerhi aranmaksızın ilâm niteliğinde belge sayılacağı, arabuluculuk faaliyeti sonunda anlaşmaya varılması hâlinde üzerinde anlaşılan hususlar hakkında taraflarca dava açılamayacağı hususları hakkında bilgi verildi.";
        yield return "Taraflara arabuluculuk faaliyeti sonunda anlaşmaları hâlinde arabuluculuk ücretinin, Arabuluculuk Asgari Ücret Tarifesinin eki Arabuluculuk Ücret Tarifesinin İkinci Kısmına göre yüzdelik dilim üzerinden nispi olarak, aksi kararlaştırılmadıkça taraflarca eşit şekilde karşılanacağı, bu durumda ücretin Tarifenin Birinci Kısmında belirlenen iki saatlik ücret tutarından az olamayacağı; arabuluculuk faaliyeti sonunda iki saatten az süren görüşmeler sonunda tarafların anlaşamamaları hâllerinde iki saatlik ücret tutarının Tarifenin Birinci Kısmına göre Adalet Bakanlığı bütçesinden ödeneceği, iki saatten fazla süren görüşmeler sonunda tarafların anlaşamamaları hâlinde ise iki saati aşan kısma ilişkin ücretin aksi kararlaştırılmadıkça taraflarca eşit şekilde uyuşmazlığın konusu dikkate alınarak Tarifenin Birinci Kısmına göre karşılanacağı, Adalet Bakanlığı bütçesinden ödenen ve taraflarca karşılanan arabuluculuk ücretinin yargılama giderlerinden sayılacağı hususları hakkında bilgi verildi.";
        yield return "Arabulucu, yapılan başvuruyu görevlendirildiği tarihten itibaren {{{SONUÇLANDIRMA SÜRESİ}}}. Arabuluculuk görüşmelerine taraflar bizzat, kanuni temsilcileri veya avukatları aracılığıyla katılabilirler.";
        yield return "Ayrıca taraflara, kendilerinden arabuluculuk sürecinde birbirlerine karşı “siz”li hitap şeklini kullanmalarının ve söz verildiği zaman sırayla ve sözleri kesilmeden konuşmalarının beklendiği, birbirlerinin sözünü kesmelerinin, söz veya hareketle diğer tarafı tahkir etmelerinin yasak olduğu, daha sonra eklemek istedikleri hususlar hakkında kendilerine konuşma olanağı tanınacağı, arabulucu tarafından da kendilerine sorular sorulabileceği hususları belirtilmiş; arabuluculuk sürecinde olabildiğince açık ve dürüst olunmasının ve iş birliği hâlinde hareket edilmesinin önemi vurgulanmış; arabuluculuk sürecinde belirtilen kurallara uymayı kabul edip etmedikleri kendilerine sorulmuştur.";
        yield return "6698 sayılı Kişisel Verilerin Korunması Kanunu m.10 uyarınca kişisel verilerin 6325 sayılı Kanun çerçevesinde yürütülen arabuluculuk sürecinin yürütülmesi amacıyla işleneceği; başvurucu ve karşı tarafça temin edilen ve arabuluculuk bürosunca sağlanan kişisel verilerin gerektiğinde diğer taraflarla paylaşılabileceği; süreç tutanaklarının Arabuluculuk Bilgi Sistemine yükleneceği ve arabulucu tarafından beş yıl süreyle saklanacağı; icra edilebilirlik şerhi veya icra takibi için yargı mercilerine sunulabileceği; ilgili kişilerin Kanunun 11. maddesinde sayılan haklara sahip oldukları bildirilmiştir.";
        yield return "Taraflar söz alarak arabuluculuğun temel ilkelerini, arabuluculuk sürecini ve arabuluculuk süreci sonunda hazırlanan arabuluculuk son tutanağının ve anlaşma belgesinin hukuki ve mali yönlerden bütün sonuçlarını anladıklarını, arabuluculuk sürecinde ve bu tutanakta belirtilen kurallara uymayı kabul ettiklerini beyan ettiler.";
        yield return "Taraflar müzakerelere başlamışlardır.";
    }

    public static IEnumerable<string> SonAnlasma(Spec s)
    {
        yield return "Taraflar arabuluculuk sürecinde yürütülen müzakereler sonucunda {{{UYUŞMAZLIK KONUSU}}} hususlarında **ANLAŞMAYA VARMIŞLARDIR.** Taraflar, anlaşma şartlarının son tutanağa değil, ayrıca düzenlenen arabuluculuk anlaşma belgesine yazılmasını kararlaştırmışlardır.";
        yield return "Taraflara üzerinde anlaşılan hususlarda dava açılamayacağı hatırlatılmış; taraflar bunu anladıklarını beyan etmişlerdir.";
    }

    public static IEnumerable<string> SonAnlasamama(Spec s)
    {
        yield return "Taraflar arabuluculuk sürecinde {{{UYUŞMAZLIK KONUSU}}} hususlarını müzakere etmiş, ancak yapılan görüşmeler sonucunda **ANLAŞMAYA VARILAMAMIŞTIR.** Görüşmeler toplam {{{GÖRÜŞME SÜRESİ}}} sürmüştür.";
        if (s.DavaSarti)
            yield return "Taraflara, anlaşmaya varılamadığına ilişkin bu son tutanağın aslının veya arabulucu tarafından onaylanmış bir örneğinin açılacak dava dilekçesine eklenmesi gerektiği hatırlatılmıştır.";
        yield return "Taraflar, son tutanağa faaliyetin sonucu dışında başka bir husus yazılmasını istememişlerdir.";
    }
}

/// <summary>
/// One Word file with the texts of the ready-made sets for legal review: the documents shared by all
/// kinds once, then the agreement document of every kind. Variables are shown as «form label».
/// </summary>
static class Review
{
    const string Shared = "İşçi - İşveren";

    public static byte[] Build(string setsFolder)
    {
        var b = new DocxBuilder();
        b.Title("EVRAKÇI HAZIR ŞABLON METİNLERİ", 28);
        b.Title("Hukuki inceleme için", 22);
        b.Empty();
        b.Para("Bu belge, Evrakçı ile birlikte gelen hazır şablonların metinlerini içerir. «…» içindeki ifadeler değişkendir; her vakada formdan girilen bilgiyle doldurulur. “(yazıyla)” ibaresi tutarın yazıyla yazılacağını gösterir. Değişkeni boş bırakılabilen satırlar (vekil, yetkili, 2.–4. taksit gibi) boş bırakıldığında belgeden çıkarılır.");
        b.Para("Bölüm 1’de bütün türlerde ortak olan belgeler bir kez (İşçi – İşveren seti üzerinden) verilmiştir. Diğer türlerde bu belgeler yalnızca kanun atfı (ör. “6102 sayılı TTK m.5/A”) ve İhtiyari sette dava şartına ilişkin cümleler bakımından farklıdır. Bölüm 2’de her türün anlaşma belgesi ayrı ayrı yer alır.");

        var sets = Directory.EnumerateDirectories(setsFolder).Select(TemplateSet.Open)
            .OrderBy(s => TemplateLibrary.CategoryRank(s.Definition.Category ?? "")).ToList();
        var shared = sets.First(s => Path.GetFileName(s.Folder) == Shared);

        b.Heading("BÖLÜM 1 – ORTAK BELGELER");
        foreach (var d in shared.Documents.Where(d => !d.Entry.Template.Contains("Anlaşma Belgesi")))
            Document(b, shared, d, $"{Path.GetFileNameWithoutExtension(d.Entry.Template)}");

        b.Heading("BÖLÜM 2 – TÜRLERE GÖRE ANLAŞMA BELGELERİ");
        foreach (var s in sets)
        {
            var d = s.Documents.First(d => d.Entry.Template.Contains("Anlaşma Belgesi"));
            var konu = s.Definition.Fields.GetValueOrDefault("UYUŞMAZLIK KONUSU")?.Default;
            Document(b, s, d, $"{s.Name} – Anlaşma Belgesi",
                $"Tür: {s.Definition.Category}. Açıklama: {s.Definition.Description} Varsayılan uyuşmazlık konusu: “{konu}”.");
        }

        b.Heading("EK – ALAN LİSTESİ (İşçi – İşveren seti)");
        foreach (var f in shared.Fields)
            b.Field(f.Label, $"{Evrakci.Core.Values.FieldTypes.DisplayName(f.Type)}" +
                (f.Definition?.Default is { } def ? $" · varsayılan: “{def}”" : "") +
                (f.Definition?.DefaultFrom is { } from ? $" · boşsa: {shared.Field(from)?.Label}" : "") +
                (f.Definition?.RemoveParagraphIfBlank == true ? " · boş bırakılabilir (satır çıkarılır)" : ""));
        return b.Build();
    }

    static void Document(DocxBuilder b, TemplateSet set, SetDocument doc, string title, string? note = null)
    {
        b.Raw("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        b.Heading("■ " + title);
        if (note is not null) b.Para($"[{note}]", DocxAlign.Left);
        foreach (var p in doc.Template!.SourceParagraphs)
        {
            var text = p.Text;
            foreach (var m in p.Variables.OrderByDescending(v => v.Start))
            {
                var f = set.Field(m.Ref.Name);
                var label = f is null ? m.Ref.Name
                    : f.Group is "Taraf-1" or "Taraf-2" or "Arabulucu" ? $"{f.Group}: {f.Label}" : f.Label;
                if (m.Ref.Modifier == Evrakci.Core.Templates.VariableModifier.Words) label += " (yazıyla)";
                text = text[..m.Start] + "«" + label + "»" + text[(m.Start + m.DelimitedLength)..];
            }
            text = text.Replace(Evrakci.Core.Templates.VariableMatcher.Opaque.ToString(), "").Replace("\t", " ").Trim();
            if (text.Length == 0) continue;
            b.Para(text.Replace("*", ""), DocxAlign.Justify, after: 60);
        }
    }
}
