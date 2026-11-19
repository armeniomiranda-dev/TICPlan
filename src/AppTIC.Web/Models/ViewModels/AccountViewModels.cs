using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Indique o email."), EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indique a palavra-passe."), DataType(DataType.Password), Display(Name = "Palavra-passe")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Manter sessão iniciada")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class AlterarPasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Palavra-passe atual")]
    public string Atual { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Nova palavra-passe")]
    public string Nova { get; set; } = string.Empty;

    [Compare(nameof(Nova), ErrorMessage = "As palavras-passe não coincidem."), DataType(DataType.Password), Display(Name = "Confirmar nova palavra-passe")]
    public string Confirmar { get; set; } = string.Empty;
}

public class UtilizadorViewModel
{
    public string? Id { get; set; }

    [Required, EmailAddress, Display(Name = "Email (login)")]
    public string Email { get; set; } = string.Empty;

    [StringLength(200), Display(Name = "Nome")]
    public string? NomeCompleto { get; set; }

    [StringLength(200), Display(Name = "Entidade")]
    public string? Entidade { get; set; }

    /// <summary>Obrigatório no perfil Utilizador: só vê a informação deste operador (ou, num grupo, dos operadores do grupo).</summary>
    [Display(Name = "Operador ou grupo")]
    public int? OperadorId { get; set; }

    [Required, Display(Name = "Perfil")]
    public string Perfil { get; set; } = Services.Roles.Utilizador;

    [Display(Name = "Ativo")]
    public bool Ativo { get; set; } = true;

    [StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Palavra-passe")]
    public string? Password { get; set; }
}

public class GtfsUploadViewModel
{
    [Required(ErrorMessage = "Indique um nome para o feed."), StringLength(200), Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [Display(Name = "Descrição")]
    public string? Descricao { get; set; }

    [Required(ErrorMessage = "Escolha o operador do feed."), Display(Name = "Operador")]
    public int? OperadorId { get; set; }

    [Required(ErrorMessage = "Escolha o ficheiro .zip."), Display(Name = "Ficheiro GTFS (.zip)")]
    public IFormFile? Ficheiro { get; set; }
}

public class ExcelUploadViewModel
{
    /// <summary>Lote a atualizar com um ficheiro novo; null = lote novo.</summary>
    public int? Id { get; set; }

    [Required(ErrorMessage = "Indique um nome para a importação."), StringLength(200), Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escolha o ficheiro .xlsx."), Display(Name = "Ficheiro Excel (.xlsx)")]
    public IFormFile? Ficheiro { get; set; }
}

/// <summary>Página de detalhe de um lote de importação.</summary>
public class ImportacaoDetalhe
{
    public required AppTIC.Web.Models.Domain.Importacao Importacao { get; init; }
    /// <summary>Circulações que pertencem atualmente ao lote.</summary>
    public int NumCirculacoes { get; init; }
    public List<RegistoRow> Registos { get; init; } = [];

    // ---- Excel ----
    public AppTIC.Web.Services.ExcelImportPreview? Preview { get; set; }
    /// <summary>O ficheiro guardado não pôde ser lido.</summary>
    public string? Erro { get; set; }

    public List<AppTIC.Web.Infrastructure.LookupItem> Feeds { get; set; } = [];
    public int? FeedId { get; set; }
    public AppTIC.Web.Services.ComparacaoGtfs? Comparacao { get; set; }
    public string? ErroComparacao { get; set; }
    /// <summary>Circulações já existentes com o trip_id das viagens correspondidas, por lote de importação.</summary>
    public List<(int? Lote, int Total)> Duplicadas { get; set; } = [];
}
