using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Реквизиты проекта Supabase. Все три значения публичные и попадают в APK
    /// намеренно: publishable-ключ и client id спроектированы как открытые, а
    /// данные защищает RLS и auth.uid(), а не их секретность. Секретный ключ
    /// (<c>sb_secret_…</c>) и client secret сюда класть нельзя ни при каких
    /// обстоятельствах — они server-only.
    ///
    /// Ассет, а не константы в коде, чтобы разделить dev и prod подменой ассета,
    /// а не правкой исходника.
    /// </summary>
    [CreateAssetMenu(fileName = "SupabaseConfig", menuName = "Mikey/Supabase Config")]
    public sealed class SupabaseConfig : ScriptableObject
    {
        /// <summary>Имя ассета в Resources, откуда его берёт <see cref="Load"/>.</summary>
        public const string ResourceName = "SupabaseConfig";

        [SerializeField] private string _url = "https://maaqlmzispqeldsjsank.supabase.co";
        [SerializeField] private string _publishableKey = "sb_publishable_2EHwQuPnOYLwa7_BTNuK8A_VKVDwpMk";
        [SerializeField] private string _googleWebClientId =
            "404020688148-53omm395re2ueeciqccpfqr400e3bds0.apps.googleusercontent.com";

        public string Url => _url;
        public string PublishableKey => _publishableKey;

        /// <summary>
        /// Web-клиент, а не Android: Credential Manager требует именно его как
        /// audience выдаваемого ID-токена, и его же ждёт Supabase.
        /// </summary>
        public string GoogleWebClientId => _googleWebClientId;

        /// <summary>Ассет из Resources, или null — тогда синхронизация просто не включается.</summary>
        public static SupabaseConfig Load() => Resources.Load<SupabaseConfig>(ResourceName);
    }
}
