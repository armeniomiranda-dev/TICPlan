using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum Ambito
{
    [Display(Name = "nacional")] Nacional = 1,
    [Display(Name = "internacional")] Internacional = 2,
    [Display(Name = "nac/int")] NacionalInternacional = 3
}
