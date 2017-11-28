﻿using MixItUp.Base;
using System;
using System.Threading.Tasks;

namespace MixItUp.WPF.Controls.Chat
{
    /// <summary>
    /// Interaction logic for ChatModerationControl.xaml
    /// </summary>
    public partial class ChatModerationControl : MainControlBase
    {
        public ChatModerationControl()
        {
            InitializeComponent();
        }

        protected override Task InitializeInternal()
        {
            this.BannedWordsTextBox.Text = string.Join(Environment.NewLine, ChannelSession.Settings.BannedWords);

            this.MaxCapsAllowedSlider.Value = ChannelSession.Settings.CapsBlockCount;
            this.MaxPunctuationAllowedSlider.Value = ChannelSession.Settings.PunctuationBlockCount;
            this.MaxEmoteAllowedSlider.Value = ChannelSession.Settings.EmoteBlockCount;
            this.BlockLinksToggleButton.IsChecked = ChannelSession.Settings.BlockLinks;

            this.Timeout1MinAfterSlider.Value = ChannelSession.Settings.Timeout1MinuteOffenseCount;
            this.Timeout5MinAfterSlider.Value = ChannelSession.Settings.Timeout5MinuteOffenseCount;

            return base.InitializeInternal();
        }

        private async void BannedWordsTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            string bannedWords = this.BannedWordsTextBox.Text;
            if (string.IsNullOrEmpty(this.BannedWordsTextBox.Text))
            {
                bannedWords = "";
            }

            ChannelSession.Settings.BannedWords.Clear();
            foreach (string split in bannedWords.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                ChannelSession.Settings.BannedWords.Add(split);
            }

            await this.Window.RunAsyncOperation(async () =>
            {
                await ChannelSession.SaveSettings();
            });
        }

        private void MaxCapsAllowedSlider_ValueChanged(object sender, int e)
        {
            ChannelSession.Settings.CapsBlockCount = (int)this.MaxCapsAllowedSlider.Value;
        }

        private void MaxPunctuationAllowedSlider_ValueChanged(object sender, int e)
        {
            ChannelSession.Settings.PunctuationBlockCount = (int)this.MaxPunctuationAllowedSlider.Value;
        }

        private void MaxEmoteAllowedSlider_ValueChanged(object sender, int e)
        {
            ChannelSession.Settings.EmoteBlockCount = (int)this.MaxEmoteAllowedSlider.Value;
        }

        private async void BlockLinksToggleButton_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            ChannelSession.Settings.BlockLinks = BlockLinksToggleButton.IsChecked.GetValueOrDefault();
            await this.Window.RunAsyncOperation(async () =>
            {
                await ChannelSession.SaveSettings();
            });
        }

        private void Timeout1MinAfterSlider_ValueChanged(object sender, int e)
        {
            ChannelSession.Settings.Timeout1MinuteOffenseCount = (int)this.Timeout1MinAfterSlider.Value;
        }

        private void Timeout5MinAfterSlider_ValueChanged(object sender, int e)
        {
            ChannelSession.Settings.Timeout5MinuteOffenseCount = (int)this.Timeout5MinAfterSlider.Value;
        }

        private async void Slider_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            await this.Window.RunAsyncOperation(async () =>
            {
                await ChannelSession.SaveSettings();
            });
        }
    }
}
