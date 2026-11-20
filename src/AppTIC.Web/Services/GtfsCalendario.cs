using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace AppTIC.Web.Services;

/// <summary>Leitura simples de ficheiros GTFS dentro do .zip (usada pelas integrações em tempo real STCP e Metro).</summary>
public static class GtfsZip
{
    private static CsvConfiguration Config => new(CultureInfo.InvariantCulture)
    {
        BadDataFound = null, MissingFieldFound = null, DetectColumnCountChanges = false
    };

    public static ZipArchiveEntry? Entrada(ZipArchive zip, string nome) =>
        zip.Entries.FirstOrDefault(e => e.Name.Equals(nome, StringComparison.OrdinalIgnoreCase));

    /// <summary>Linhas do ficheiro como dicionários coluna → valor (para ficheiros pequenos).</summary>
    public static IEnumerable<Dictionary<string, string>> Ler(ZipArchive zip, string nome)
    {
        if (Entrada(zip, nome) is not { } entry) yield break;
        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, Config);
        if (!csv.Read()) yield break;
        csv.ReadHeader();
        var cab = csv.HeaderRecord!.Select(h => h.Trim().TrimStart('﻿')).ToArray();
        while (csv.Read())
        {
            var linha = new Dictionary<string, string>(cab.Length);
            for (var i = 0; i < cab.Length; i++) linha[cab[i]] = csv.GetField(i) ?? "";
            yield return linha;
        }
    }

    /// <summary>Leitura rápida de stop_times.txt (sem um dicionário por linha).</summary>
    public static IEnumerable<(string Trip, string Paragem, int Seq, int Hora)> LerHorarios(ZipArchive zip)
    {
        if (Entrada(zip, "stop_times.txt") is not { } entry) yield break;
        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, Config);
        if (!csv.Read()) yield break;
        csv.ReadHeader();
        var cab = csv.HeaderRecord!.Select(h => h.Trim().TrimStart('﻿')).ToList();
        int iTrip = cab.IndexOf("trip_id"), iStop = cab.IndexOf("stop_id"), iSeq = cab.IndexOf("stop_sequence"),
            iChegada = cab.IndexOf("arrival_time"), iPartida = cab.IndexOf("departure_time");
        while (csv.Read())
        {
            var hora = iChegada >= 0 ? csv.GetField(iChegada) : null;
            if (string.IsNullOrEmpty(hora) && iPartida >= 0) hora = csv.GetField(iPartida);
            yield return (csv.GetField(iTrip)!, csv.GetField(iStop)!,
                int.Parse(csv.GetField(iSeq)!, CultureInfo.InvariantCulture), Segundos(hora ?? ""));
        }
    }

    /// <summary>"H:MM:SS" (pode passar das 24h) → segundos desde o início do dia de serviço; -1 se vazio/inválido.</summary>
    public static int Segundos(string hhmmss)
    {
        var p = hhmmss.Trim().Split(':');
        return p.Length == 3 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m) && int.TryParse(p[2], out var s)
            ? h * 3600 + m * 60 + s : -1;
    }

    public static DateOnly Data(string yyyymmdd) => DateOnly.ParseExact(yyyymmdd.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Instante = meia-noite local do dia de serviço + segundos do GTFS.</summary>
    public static DateTimeOffset Instante(DateOnly diaServico, int segundos, TimeZoneInfo fuso)
    {
        var meiaNoite = diaServico.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(meiaNoite, fuso.GetUtcOffset(meiaNoite)).AddSeconds(segundos);
    }
}

/// <summary>calendar.txt + calendar_dates.txt: que serviços circulam em cada dia.</summary>
public class GtfsCalendario
{
    private readonly Dictionary<string, (HashSet<DayOfWeek> Dias, DateOnly Inicio, DateOnly Fim)> _semanal = [];
    private readonly Dictionary<(string ServiceId, DateOnly Data), bool> _excecoes = [];

    public static GtfsCalendario Carregar(ZipArchive zip)
    {
        var cal = new GtfsCalendario();
        (string Col, DayOfWeek Dia)[] colunas =
        [
            ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday),
            ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday), ("sunday", DayOfWeek.Sunday)
        ];
        foreach (var r in GtfsZip.Ler(zip, "calendar.txt"))
            cal._semanal[r["service_id"]] = (colunas.Where(x => r.GetValueOrDefault(x.Col) == "1").Select(x => x.Dia).ToHashSet(),
                GtfsZip.Data(r["start_date"]), GtfsZip.Data(r["end_date"]));
        foreach (var r in GtfsZip.Ler(zip, "calendar_dates.txt"))
            cal._excecoes[(r["service_id"], GtfsZip.Data(r["date"]))] = r["exception_type"] == "1";
        return cal;
    }

    public bool Ativo(string serviceId, DateOnly data)
    {
        if (_excecoes.TryGetValue((serviceId, data), out var ativo)) return ativo;
        return _semanal.TryGetValue(serviceId, out var c) && data >= c.Inicio && data <= c.Fim && c.Dias.Contains(data.DayOfWeek);
    }

    /// <summary>Último dia com serviço previsto (para avisar quando o GTFS expira).</summary>
    public DateOnly? Fim => _semanal.Values.Select(c => (DateOnly?)c.Fim)
        .Concat(_excecoes.Where(e => e.Value).Select(e => (DateOnly?)e.Key.Data)).Max();
}
