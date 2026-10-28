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

    /// <summary>Obrigatório no perfil Utilizador: só vê a informação deste operador.</summary>
    [Display(Name = "Operador")]
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
