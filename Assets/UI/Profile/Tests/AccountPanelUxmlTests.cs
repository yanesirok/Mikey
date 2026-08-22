using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Блок аккаунта существует в вёрстке и содержит всё, что требует спека:
    /// состояние, вход, выход, удаление и текст согласия.
    /// </summary>
    public class AccountPanelUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";

        private static string Uxml => File.ReadAllText(UxmlPath);

        [Test]
        public void ProfileDetails_HasEveryAccountControl()
        {
            string uxml = Uxml;
            foreach (string name in new[]
                     {
                         "account-status", "account-sign-in", "account-sign-out",
                         "account-delete", "account-consent",
                     })
            {
                StringAssert.Contains($"name=\"{name}\"", uxml,
                    $"В вёрстке нет элемента '{name}'.");
            }
        }

        [Test]
        public void ConsentText_NamesTheBodyDataThatLeavesTheDevice()
        {
            string uxml = Uxml;
            int start = uxml.IndexOf("name=\"account-consent\"", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "Текста согласия нет.");

            string tail = uxml.Substring(start, System.Math.Min(600, uxml.Length - start));
            StringAssert.Contains("возраст", tail,
                "Согласие обязано называть данные о теле: человек отправляет их о себе.");
            StringAssert.Contains("вес", tail);
            StringAssert.Contains("рост", tail);
        }
    }
}
