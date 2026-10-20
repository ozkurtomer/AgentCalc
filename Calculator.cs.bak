using System.Globalization;
using System.Text.Json;

namespace AgentCalc;

public enum Arity { None, One, Two }

public sealed record CalcTool(string Name, string Description, Arity Arity, Func<double[], double> Run);

public static class Calculator
{
    public static readonly IReadOnlyList<CalcTool> Tools = new List<CalcTool>
    {
        // İki sayılı işlemler
        new("topla", "İki sayıyı toplar", Arity.Two, p => p[0] + p[1]),
        new("cikar", "İki sayıyı çıkarır: a - b", Arity.Two, p => p[0] - p[1]),
        new("carp", "İki sayıyı çarpar", Arity.Two, p => p[0] * p[1]),
        new("bol", "İki sayıyı böler: a / b", Arity.Two, p => Bol(p[0], p[1])),
        new("us", "a sayısının b. kuvvetini alır: a üssü b", Arity.Two, p => Math.Pow(p[0], p[1])),
        new("kok", "a sayısının b. dereceden kökünü alır", Arity.Two, p => Kok(p[0], p[1])),
        new("mod", "a sayısını b'ye böldüğünde kalanı verir", Arity.Two, p => Mod(p[0], p[1])),
        new("tamBolum", "a sayısını b'ye böldüğünde tam bölüm kısmını verir", Arity.Two, p => TamBolum(p[0], p[1])),
        new("yuzde", "a sayısının b yüzdesini hesaplar", Arity.Two, p => p[0] * p[1] / 100.0),
        new("yuzdeOrani", "a sayısının b sayısının yüzde kaçı olduğunu hesaplar", Arity.Two, p => YuzdeOrani(p[0], p[1])),
        new("ebob", "İki tam sayının en büyük ortak bölenini bulur", Arity.Two, p => Ebob(p[0], p[1])),
        new("ekok", "İki tam sayının en küçük ortak katını bulur", Arity.Two, p => Ekok(p[0], p[1])),
        new("max", "İki sayıdan büyük olanı verir", Arity.Two, p => Math.Max(p[0], p[1])),
        new("min", "İki sayıdan küçük olanı verir", Arity.Two, p => Math.Min(p[0], p[1])),
        new("ortalama", "İki sayının ortalamasını alır", Arity.Two, p => (p[0] + p[1]) / 2.0),
        new("log", "a sayısının b tabanına göre logaritmasını alır", Arity.Two, p => Log(p[0], p[1])),
        new("hipotenus", "Dik kenarları a ve b olan üçgenin hipotenüsünü hesaplar", Arity.Two, p => Math.Sqrt(p[0] * p[0] + p[1] * p[1])),

        // Tek sayılı işlemler
        new("karekok", "Bir sayının karekökünü alır", Arity.One, p => Karekok(p[0])),
        new("kare", "Bir sayının karesini alır", Arity.One, p => p[0] * p[0]),
        new("kup", "Bir sayının küpünü alır", Arity.One, p => p[0] * p[0] * p[0]),
        new("faktoriyel", "0 ile 170 arasındaki bir tam sayının faktöriyelini alır", Arity.One, p => Faktoriyel(p[0])),
        new("fibonacci", "n. Fibonacci sayısını verir (n en fazla 90)", Arity.One, p => Fibonacci(p[0])),
        new("asalMi", "Tam sayı asalsa 1, değilse 0 verir", Arity.One, p => AsalMi(p[0])),
        new("mutlak", "Bir sayının mutlak değerini verir", Arity.One, p => Math.Abs(p[0])),
        new("tersi", "Bir sayının tersini (1 bölü a) verir", Arity.One, p => Tersi(p[0])),
        new("yuvarla", "Bir sayıyı en yakın tam sayıya yuvarlar", Arity.One, p => Math.Round(p[0], MidpointRounding.AwayFromZero)),
        new("tavan", "Bir sayıdan büyük ya da eşit en küçük tam sayıyı verir", Arity.One, p => Math.Ceiling(p[0])),
        new("taban", "Bir sayıdan küçük ya da eşit en büyük tam sayıyı verir", Arity.One, p => Math.Floor(p[0])),
        new("ln", "Doğal logaritma (taban e)", Arity.One, p => Ln(p[0])),
        new("log10", "10 tabanına göre logaritma", Arity.One, p => Log(p[0], 10)),
        new("sin", "Açının sinüsü (derece cinsinden)", Arity.One, p => Math.Sin(p[0] * Math.PI / 180)),
        new("cos", "Açının kosinüsü (derece cinsinden)", Arity.One, p => Math.Cos(p[0] * Math.PI / 180)),
        new("tan", "Açının tanjantı (derece cinsinden)", Arity.One, p => Math.Tan(p[0] * Math.PI / 180)),
        new("radyan", "Dereceyi radyana çevirir", Arity.One, p => p[0] * Math.PI / 180),
        new("derece", "Radyanı dereceye çevirir", Arity.One, p => p[0] * 180 / Math.PI),

        // Parametresiz sabitler
        new("pi", "Pi sayısının değerini verir", Arity.None, _ => Math.PI),
        new("e", "Euler sayısının değerini verir", Arity.None, _ => Math.E),
    };

    // Modele giden araç şemasını kayıttan üretir
    public static object Schema(CalcTool t)
    {
        var names = t.Arity switch
        {
            Arity.One => new[] { "a" },
            Arity.Two => new[] { "a", "b" },
            _ => Array.Empty<string>()
        };

        var props = names.ToDictionary(n => n, n => (object)new
        {
            type = "number",
            description = names.Length == 1 ? "sayı" : n == "a" ? "ilk sayı" : "ikinci sayı"
        });

        return new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = new { type = "object", properties = props, required = names }
            }
        };
    }

    // Model çağrısının argümanlarını okuyup doğru işlemi çalıştırır
    public static string Run(string name, JsonElement args)
    {
        var tool = Tools.FirstOrDefault(t => t.Name == name);
        if (tool is null) return $"Bilinmeyen araç: {name}";

        var p = tool.Arity switch
        {
            Arity.One => new[] { args.GetProperty("a").GetDouble() },
            Arity.Two => new[] { args.GetProperty("a").GetDouble(), args.GetProperty("b").GetDouble() },
            _ => Array.Empty<double>()
        };

        var sonuc = tool.Run(p);
        if (double.IsNaN(sonuc) || double.IsInfinity(sonuc))
            return "Bu işlemin sonucu tanımlı değil.";

        return sonuc.ToString(CultureInfo.InvariantCulture);
    }

    // ---------- İşlem yardımcıları ----------

    static double Tam(double x, string ad)
    {
        if (x != Math.Floor(x)) throw new InvalidOperationException($"{ad} için tam sayı girin.");
        return x;
    }

    static double Bol(double a, double b)
    {
        if (b == 0) throw new InvalidOperationException("Sıfıra bölme yapılamaz.");
        return a / b;
    }

    static double Mod(double a, double b)
    {
        if (b == 0) throw new InvalidOperationException("Sıfıra göre mod alınamaz.");
        return a % b;
    }

    static double TamBolum(double a, double b)
    {
        if (b == 0) throw new InvalidOperationException("Sıfıra bölme yapılamaz.");
        return Math.Floor(a / b);
    }

    static double YuzdeOrani(double a, double b)
    {
        if (b == 0) throw new InvalidOperationException("Sıfır sayısının yüzdesi alınamaz.");
        return a / b * 100;
    }

    static double Kok(double a, double b)
    {
        if (b == 0) throw new InvalidOperationException("0. dereceden kök tanımsızdır.");
        return Math.Pow(a, 1 / b);
    }

    static double Karekok(double a)
    {
        if (a < 0) throw new InvalidOperationException("Negatif sayının reel karekökü yoktur.");
        return Math.Sqrt(a);
    }

    static double Log(double a, double b)
    {
        if (a <= 0 || b <= 0 || b == 1)
            throw new InvalidOperationException("Logaritma için a ve b pozitif olmalı, b 1 olamaz.");
        return Math.Log(a, b);
    }

    static double Ln(double a)
    {
        if (a <= 0) throw new InvalidOperationException("Logaritma için sayı pozitif olmalı.");
        return Math.Log(a);
    }

    static double Tersi(double a)
    {
        if (a == 0) throw new InvalidOperationException("0'ın tersi tanımsızdır.");
        return 1 / a;
    }

    static double Ebob(double a, double b)
    {
        long x = Math.Abs((long)Tam(a, "EBOB"));
        long y = Math.Abs((long)Tam(b, "EBOB"));
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }
        return x;
    }

    static double Ekok(double a, double b)
    {
        long x = Math.Abs((long)Tam(a, "EKOK"));
        long y = Math.Abs((long)Tam(b, "EKOK"));
        if (x == 0 || y == 0) return 0;
        long g = (long)Ebob(x, y);
        return (double)(x / g * y);
    }

    static double Faktoriyel(double a)
    {
        var n = (int)Tam(a, "Faktöriyel");
        if (n < 0 || n > 170)
            throw new InvalidOperationException("Faktöriyel için 0 ile 170 arasında tam sayı girin.");

        double sonuc = 1;
        for (int i = 2; i <= n; i++) sonuc *= i;
        return sonuc;
    }

    static double Fibonacci(double a)
    {
        var n = (int)Tam(a, "Fibonacci");
        if (n < 0 || n > 90)
            throw new InvalidOperationException("Fibonacci için n 0 ile 90 arasında olmalı.");

        long x = 0, y = 1;
        for (int i = 0; i < n; i++)
        {
            (x, y) = (y, x + y);
        }
        return x;
    }

    static double AsalMi(double a)
    {
        var n = (long)Tam(a, "Asallık");
        if (n < 2) return 0;
        for (long i = 2; i * i <= n; i++)
        {
            if (n % i == 0) return 0;
        }
        return 1;
    }
}