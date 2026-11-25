using System;

namespace ApexHUD.Core.Telemetry;

public enum CarBrand
{
    Unknown = 0,
    Porsche,
    Ferrari,
    Bmw,
    Mercedes,
    Audi,
    McLaren,
    Lamborghini,
    Ford,
    Chevrolet,
    AstonMartin,
    Acura,
    Honda,
    Cadillac,
    Toyota,
    Mazda,
    Dallara,
    Hyundai,
    Lotus,
    Nissan,
    Renault,
    Volkswagen,
    Formula
}

public static class CarBrandResolver
{
    public static CarBrand Resolve(string? screenName, string? carPath)
    {
        string s = $"{screenName} {carPath}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return CarBrand.Unknown;

        if (s.Contains("porsche") || s.Contains("ruf")) return CarBrand.Porsche;
        if (s.Contains("ferrari")) return CarBrand.Ferrari;
        if (s.Contains("bmw")) return CarBrand.Bmw;
        if (s.Contains("mercedes") || s.Contains("amg")) return CarBrand.Mercedes;
        if (s.Contains("audi")) return CarBrand.Audi;
        if (s.Contains("mclaren")) return CarBrand.McLaren;
        if (s.Contains("lamborghini") || s.Contains("huracan")) return CarBrand.Lamborghini;
        if (s.Contains("ford") || s.Contains("mustang")) return CarBrand.Ford;
        if (s.Contains("chevrolet") || s.Contains("chevy") || s.Contains("corvette") || s.Contains("camaro")) return CarBrand.Chevrolet;
        if (s.Contains("aston")) return CarBrand.AstonMartin;
        if (s.Contains("acura")) return CarBrand.Acura;
        if (s.Contains("honda") || s.Contains("civic")) return CarBrand.Honda;
        if (s.Contains("cadillac")) return CarBrand.Cadillac;
        if (s.Contains("toyota") || s.Contains("gr86") || s.Contains("supra") || s.Contains("camry")) return CarBrand.Toyota;
        if (s.Contains("mazda") || s.Contains("mx5") || s.Contains("mx-5")) return CarBrand.Mazda;
        if (s.Contains("dallara") || s.Contains("p217") || s.Contains("f3") || s.Contains("ir18") || s.Contains("il15")) return CarBrand.Dallara;
        if (s.Contains("hyundai") || s.Contains("elantra") || s.Contains("veloster")) return CarBrand.Hyundai;
        if (s.Contains("lotus")) return CarBrand.Lotus;
        if (s.Contains("nissan")) return CarBrand.Nissan;
        if (s.Contains("renault") || s.Contains("clio")) return CarBrand.Renault;
        if (s.Contains("volkswagen") || s.Contains("vw") || s.Contains("jetta")) return CarBrand.Volkswagen;
        if (s.Contains("formula") || s.Contains("fia f4") || s.Contains("super formula") || s.Contains("vee") || s.Contains("skip")) return CarBrand.Formula;

        return CarBrand.Unknown;
    }
}
