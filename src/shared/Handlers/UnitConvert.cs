using System;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// Unit conversion at the handler boundary.
/// The Inventor API uses internal <b>centimetres</b> for length (and cm^3 for volume, radians for angles),
/// while every MCP tool input/output in this gateway is expressed in mm / mm^3 / degrees.
/// Convert mm -> cm before calling the API, and cm -> mm on the way out.
/// </summary>
public static class UnitConvert
{
    // Length: mm <-> cm (Inventor internal length unit is cm).
    public static double MmToCm(double mm) => mm / 10.0;
    public static double CmToMm(double cm) => cm * 10.0;

    // Volume: mm^3 <-> cm^3.
    public static double Mm3ToCm3(double mm3) => mm3 / 1000.0;
    public static double Cm3ToMm3(double cm3) => cm3 * 1000.0;

    // Area: mm^2 <-> cm^2.
    public static double Mm2ToCm2(double mm2) => mm2 / 100.0;
    public static double Cm2ToMm2(double cm2) => cm2 * 100.0;

    // Angle: degrees <-> radians (Inventor API angle unit is radians).
    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    // Mass: kg <-> g (Inventor internal mass unit is kg).
    public static double KgToG(double kg) => kg * 1000.0;
    public static double GToKg(double g) => g / 1000.0;
}
