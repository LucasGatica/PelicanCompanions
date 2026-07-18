#if PELICAN_TEST_STUBS
namespace StardewModdingAPI
{
    /// <summary>Minimal marker used by the GMCM compatibility contract.</summary>
    public interface IManifest
    {
    }
}

namespace StardewModdingAPI.Utilities
{
    /// <summary>
    /// Minimal test double for the SMAPI keybind list. Production code only
    /// needs <see cref="Parse"/> while validating configuration defaults.
    /// </summary>
    public sealed class KeybindList
    {
        private KeybindList(string value)
        {
            this.Value = value;
        }

        public string Value { get; }

        public static KeybindList Parse(string value)
        {
            return new KeybindList(value);
        }

        public override string ToString()
        {
            return this.Value;
        }
    }
}
#endif
