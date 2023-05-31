using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;

namespace OdinOnDemand.Utils
{
    public class KeyConfig
    {

        // Variable button backed by a KeyCode and a GamepadButton config
        private static ConfigEntry<KeyCode> _useRemoteConfig;
        public static ButtonConfig UseRemoteButton;

        private static ConfigEntry<KeyCode> _linkRemoteConfig;
        public static ButtonConfig LinkRemoteButton;
        
        private static ConfigEntry<KeyCode> _changeLinkModeConfig;
        public static ButtonConfig ChangeLinkModeButton;

        // Create configuration values
        public static void SetupKeyConfig(ConfigFile config)
        {
            config.SaveOnConfigSet = true;
            
            _useRemoteConfig = config.Bind("Keymap", "Use Screen", KeyCode.Mouse0, new ConfigDescription("Key to use the screen currently looking at."));
            //_linkRemoteConfig = config.Bind("Keymap", "Link", KeyCode.Mouse1, new ConfigDescription("Key to link screens to speakers."));
            //_changeLinkModeConfig = config.Bind("Keymap", "Change Mode", KeyCode.F, new ConfigDescription("Key to change mode of linking."));
            
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
            /*
            LinkRemoteButton = new ButtonConfig
            {
                Name = "RemoteLink",
                Config = _linkRemoteConfig,        // Keyboard input
                HintToken = "$remote_linkhint",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            ChangeLinkModeButton = new ButtonConfig
            {
                Name = "RemoteChangeLinkMode",
                Config = _changeLinkModeConfig,        // Keyboard input
                HintToken = "$remote_changelinkmodehint",        // Displayed KeyHint
                BlockOtherInputs = false   // Blocks all other input for this Key / Button
            };
            */
            InputManager.Instance.AddButton(OdinOnDemandPlugin.PluginGUID, UseRemoteButton);
            
            //InputManager.Instance.AddButton(ValMedia.PluginGUID, LinkRemoteButton);
            //InputManager.Instance.AddButton(ValMedia.PluginGUID, ChangeLinkModeButton);
        }
        private static void KeyHintsRemote()
        {
            // Create custom KeyHints for the item
            KeyHintConfig keyhintRemote = new KeyHintConfig
            {
                Item = "remote",
                ButtonConfigs = new[]
                {
                    UseRemoteButton
                    //, LinkRemoteButton, ChangeLinkModeButton
                }
            };
            KeyHintManager.Instance.AddKeyHint(keyhintRemote);
        }
    }
}