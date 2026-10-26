using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AppTIC.Web.Controllers;

public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IActivityLog log) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is null || !user.Ativo)
        {
            ModelState.AddModelError(string.Empty, "Email ou palavra-passe inválidos.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await log.LogAsync(CategoriaRegisto.Sessao, "Login", "Sessão iniciada.", ownerId: user.Id, userName: user.UserName);
            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
        }

        await log.LogAsync(CategoriaRegisto.Sessao, "Login falhado",
            result.IsLockedOut ? "Conta bloqueada temporariamente após várias tentativas falhadas." : "Palavra-passe errada.",
            NivelRegisto.Aviso, ownerId: user.Id, userName: user.UserName);
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Conta bloqueada temporariamente. Tente novamente dentro de 15 minutos."
            : "Email ou palavra-passe inválidos.");
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await log.LogAsync(CategoriaRegisto.Sessao, "Logout", "Sessão terminada.");
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    public IActionResult AlterarPassword() => View(new AlterarPasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AlterarPassword(AlterarPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await userManager.ChangePasswordAsync(user, model.Atual, model.Nova);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }
        await signInManager.RefreshSignInAsync(user);
        await log.LogAsync(CategoriaRegisto.Sessao, "Palavra-passe", "Palavra-passe alterada.");
        TempData["Ok"] = "Palavra-passe alterada.";
        return RedirectToAction("Index", "Home");
    }
}
