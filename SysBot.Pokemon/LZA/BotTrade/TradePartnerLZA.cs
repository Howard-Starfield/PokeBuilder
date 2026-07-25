using PKHeX.Core;
using System;
using System.Buffers.Binary;
using System.Diagnostics;

namespace SysBot.Pokemon;

public sealed class TradePartnerLZA
{
    public ulong NID { get; }
    public int Gender { get; }
    public byte Language { get; }
    public string GenderString => TrainerDisplayHelper.GetGenderString(Gender);
    public string LanguageString => TrainerDisplayHelper.GetLanguageString(Language);
    public string TID7 { get; }
    public string SID7 { get; }
    public string TrainerName { get; }

    public TradePartnerLZA(ulong ID, byte[] TIDSID, byte[] trainerNameObject)
    {
        NID = ID;

        Debug.Assert(TIDSID.Length == 4);
        var tidsid = BitConverter.ToUInt32(TIDSID, 0);
        TID7 = $"{tidsid % 1_000_000:000000}";
        SID7 = $"{tidsid / 1_000_000:0000}";

        Gender = -1;
        Language = 0;
        TrainerName = StringConverter8.GetString(trainerNameObject);
    }

    public TradePartnerLZA(ulong id, TradePartnerStatusLZA info)
    {
        NID = id;
        Gender = info.Gender;
        Language = info.Language;
        TID7 = info.DisplayTID.ToString("D6");
        SID7 = info.DisplaySID.ToString("D4");
        TrainerName = info.OT;
    }

    public const int MaxByteLengthStringObject = 26;
}

public sealed class TradePartnerStatusLZA
{
    public readonly byte[] Data = new byte[0x30];

    public uint DisplaySID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) / 1_000_000;

    public uint DisplayTID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) % 1_000_000;

    public int Gender => Data[0x04];

    public byte Language => Data[0x05];

    public string OT => StringConverter8.GetString(Data.AsSpan(0x08, 0x1A));
}

public static class TrainerDisplayHelper
{
    public static string GetGenderString(int gender) => gender switch
    {
        0 => "Male",
        1 => "Female",
        _ => $"Unknown ({gender})"
    };

    public static string GetLanguageString(int language)
    {
        byte langByte = (byte)language;

        if (Enum.IsDefined(typeof(LanguageID), langByte))
            return ((LanguageID)langByte).ToString();

        return $"Unknown ({language})";
    }
}
