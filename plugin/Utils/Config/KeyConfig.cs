using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;

namespace OdinOnDemand.Utils.Config
{
    public class KeyConfig
    {

        // Variable button backed by a KeyCode and a GamepadButton config
        private static ConfigEntry<KeyCode> _useRemoteConfig;
        public static ButtonConfig UseRemoteButton;

        private static ConfigEntry<KeyCode> _linkRemoteConfig;
        public static ButtonConfig LinkRemoteButton;
        
        
        // Create configuration values
        public static void SetupKeyConfig(ConfigFile config)
        {
            config.SaveOnConfigSet = true;
            
            _useRemoteConfig = config.Bind("Keymap", "Use Screen", KeyCode.Mouse0, new ConfigDescription("Key to use the screen currently looking at."));
            _linkRemoteConfig = config.Bind("Keymap", "Link", KeyCode.Mouse1, new ConfigDescription("Key to link screens to speakers."));
         
            AddInputs();
            KeyHintsRemote();
        }

        private static void AddInputs()
        {
            UseRemoteButton = new ButtonConfig
            {
                Name = "RemoteUseScreen",
                Config = _useRemoteConfig,        // Keyboard input
                HintToken = "$remote_usehint",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            LinkRemoteButton = new ButtonConfig
            {
                Name = "RemoteLink",
                Config = _linkRemoteConfig,        // Keyboard input
                HintToken = "$remote_linkhint",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            
            InputManager.Instance.AddButton(OdinOnDemandPlugin.PluginGUID, UseRemoteButton);
            
            InputManager.Instance.AddButton(OdinOnDemandPlugin.PluginGUID, LinkRemoteButton);
            
        }
        private static void KeyHintsRemote()
        {
            // Create custom KeyHints for the item
            KeyHintConfig keyhintRemote = new KeyHintConfig
            {
                Item = "remotecontrol",
                ButtonConfigs = new[]
                {
                    UseRemoteButton, LinkRemoteButton
                }
            };
            KeyHintManager.Instance.AddKeyHint(keyhintRemote);
        }
    }
}