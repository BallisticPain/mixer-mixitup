﻿using Mixer.Base.Util;
using MixItUp.Base;
using MixItUp.Base.Actions;
using MixItUp.Base.Services;
using MixItUp.WPF.Util;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MixItUp.WPF.Controls.Actions
{
    /// <summary>
    /// Interaction logic for StreamingSoftwareActionControl.xaml
    /// </summary>
    public partial class StreamingSoftwareActionControl : ActionControlBase
    {
        private StreamingSoftwareAction action;
        private ObservableCollection<string> actionTypes = new ObservableCollection<string>();

        public StreamingSoftwareActionControl(ActionContainerControl containerControl) : base(containerControl) { InitializeComponent(); }

        public StreamingSoftwareActionControl(ActionContainerControl containerControl, StreamingSoftwareAction action) : this(containerControl) { this.action = action; }

        public override Task OnLoaded()
        {
            this.StreamingSoftwareComboBox.ItemsSource = EnumHelper.GetEnumNames<StreamingSoftwareTypeEnum>();

            this.StreamingSoftwareComboBox.SelectedItem = EnumHelper.GetEnumName(StreamingSoftwareTypeEnum.DefaultSetting);
            this.SourceVisibleCheckBox.IsChecked = true;
            this.SourceDimensionsXScaleTextBox.Text = "1";
            this.SourceDimensionsYScaleTextBox.Text = "1";

            if (this.action != null)
            {
                this.StreamingSoftwareComboBox.SelectedItem = EnumHelper.GetEnumName(this.action.SoftwareType);
                this.StreamingActionTypeComboBox.SelectedItem = EnumHelper.GetEnumName(this.action.ActionType);

                if (this.action.ActionType == StreamingActionTypeEnum.Scene)
                {
                    this.SceneNameTextBox.Text = this.action.SceneName;
                }
                else if (this.action.ActionType == StreamingActionTypeEnum.StartStopStream)
                {
                    // Do nothing...
                }
                else
                {
                    this.SourceNameTextBox.Text = this.action.SourceName;
                    this.SourceVisibleCheckBox.IsChecked = this.action.SourceVisible;
                    if (this.action.ActionType == StreamingActionTypeEnum.TextSource)
                    {
                        this.SourceTextTextBox.Text = this.action.SourceText;
                        this.SourceLoadTextFromTextBox.Text = this.action.SourceTextFilePath;
                    }
                    else if (this.action.ActionType == StreamingActionTypeEnum.WebBrowserSource)
                    {
                        this.SourceWebPageTextBox.Text = this.action.SourceURL;
                    }
                    else if (this.action.ActionType == StreamingActionTypeEnum.SourceDimensions)
                    {
                        this.SourceDimensionsXPositionTextBox.Text = this.action.SourceDimensions.X.ToString();
                        this.SourceDimensionsYPositionTextBox.Text = this.action.SourceDimensions.Y.ToString();
                        this.SourceDimensionsRotationTextBox.Text = this.action.SourceDimensions.Rotation.ToString();
                        this.SourceDimensionsXScaleTextBox.Text = this.action.SourceDimensions.XScale.ToString();
                        this.SourceDimensionsYScaleTextBox.Text = this.action.SourceDimensions.YScale.ToString();
                    }
                }
            }

            return Task.FromResult(0);
        }

        public override ActionBase GetAction()
        {
            if (this.StreamingSoftwareComboBox.SelectedIndex >= 0 && this.StreamingActionTypeComboBox.SelectedIndex >= 0)
            {
                StreamingSoftwareTypeEnum software = EnumHelper.GetEnumValueFromString<StreamingSoftwareTypeEnum>((string)this.StreamingSoftwareComboBox.SelectedItem);
                StreamingActionTypeEnum type = EnumHelper.GetEnumValueFromString<StreamingActionTypeEnum>((string)this.StreamingActionTypeComboBox.SelectedItem);

                if (type == StreamingActionTypeEnum.Scene && !string.IsNullOrEmpty(this.SceneNameTextBox.Text))
                {
                    return StreamingSoftwareAction.CreateSceneAction(software, this.SceneNameTextBox.Text);
                }
                else if (type == StreamingActionTypeEnum.StartStopStream)
                {
                    return StreamingSoftwareAction.CreateStartStopStreamAction(software);
                }
                else if (!string.IsNullOrEmpty(this.SourceNameTextBox.Text))
                {
                    if (type == StreamingActionTypeEnum.TextSource)
                    {
                        if (!string.IsNullOrEmpty(this.SourceTextTextBox.Text) && !string.IsNullOrEmpty(this.SourceLoadTextFromTextBox.Text))
                        {
                            StreamingSoftwareAction action = StreamingSoftwareAction.CreateTextSourceAction(software, this.SourceNameTextBox.Text, this.SourceVisibleCheckBox.IsChecked.GetValueOrDefault(), this.SourceTextTextBox.Text, this.SourceLoadTextFromTextBox.Text);
                            action.UpdateReferenceTextFile(string.Empty);
                            return action;
                        }
                    }
                    else if (type == StreamingActionTypeEnum.WebBrowserSource)
                    {
                        if (!string.IsNullOrEmpty(this.SourceWebPageTextBox.Text))
                        {
                            return StreamingSoftwareAction.CreateWebBrowserSourceAction(software, this.SourceNameTextBox.Text, this.SourceVisibleCheckBox.IsChecked.GetValueOrDefault(), this.SourceWebPageTextBox.Text);
                        }
                    }
                    else if (type == StreamingActionTypeEnum.SourceDimensions)
                    {
                        int x, y, rotation;
                        float xScale, yScale;
                        if (int.TryParse(this.SourceDimensionsXPositionTextBox.Text, out x) && int.TryParse(this.SourceDimensionsYPositionTextBox.Text, out y) &&
                            int.TryParse(this.SourceDimensionsRotationTextBox.Text, out rotation) && float.TryParse(this.SourceDimensionsXScaleTextBox.Text, out xScale) &&
                            float.TryParse(this.SourceDimensionsYScaleTextBox.Text, out yScale))
                        {
                            return StreamingSoftwareAction.CreateSourceDimensionsAction(software, this.SourceNameTextBox.Text, this.SourceVisibleCheckBox.IsChecked.GetValueOrDefault(),
                                new StreamingSourceDimensions() { X = x, Y = y, Rotation = rotation, XScale = xScale, YScale = yScale });
                        }
                    }
                    else
                    {
                        return StreamingSoftwareAction.CreateSourceVisibilityAction(software, this.SourceNameTextBox.Text, this.SourceVisibleCheckBox.IsChecked.GetValueOrDefault());
                    }
                }
            }
            return null;
        }

        private void StreamingSoftwareComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.OBSStudioNotEnabledWarningTextBlock.Visibility = Visibility.Collapsed;
            this.XSplitNotEnabledWarningTextBlock.Visibility = Visibility.Collapsed;
            this.StreamlabsOBSNotEnabledWarningTextBlock.Visibility = Visibility.Collapsed;

            if (this.StreamingSoftwareComboBox.SelectedIndex >= 0)
            {
                StreamingSoftwareTypeEnum software = this.GetSelectedSoftware();
                if (software == StreamingSoftwareTypeEnum.OBSStudio)
                {
                    this.OBSStudioNotEnabledWarningTextBlock.Visibility = (ChannelSession.Services.OBSWebsocket == null) ? Visibility.Visible : Visibility.Collapsed;
                    this.StreamingActionTypeComboBox.ItemsSource = EnumHelper.GetEnumNames<StreamingActionTypeEnum>();
                }
                else if (software == StreamingSoftwareTypeEnum.XSplit)
                {
                    this.XSplitNotEnabledWarningTextBlock.Visibility = (ChannelSession.Services.XSplitServer == null) ? Visibility.Visible : Visibility.Collapsed;
                    this.StreamingActionTypeComboBox.ItemsSource = EnumHelper.GetEnumNames<StreamingActionTypeEnum>(new List<StreamingActionTypeEnum>()
                        { StreamingActionTypeEnum.Scene, StreamingActionTypeEnum.SourceVisibility, StreamingActionTypeEnum.TextSource, StreamingActionTypeEnum.WebBrowserSource });
                }
                else if (software == StreamingSoftwareTypeEnum.StreamlabsOBS)
                {
                    this.StreamlabsOBSNotEnabledWarningTextBlock.Visibility = (ChannelSession.Services.StreamlabsOBSService == null) ? Visibility.Visible : Visibility.Collapsed;
                    this.StreamingActionTypeComboBox.ItemsSource = EnumHelper.GetEnumNames<StreamingActionTypeEnum>(new List<StreamingActionTypeEnum>()
                        { StreamingActionTypeEnum.Scene, StreamingActionTypeEnum.SourceVisibility, StreamingActionTypeEnum.TextSource, StreamingActionTypeEnum.SourceDimensions, StreamingActionTypeEnum.StartStopStream });
                }
            }
        }

        private void StreamingActionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.StreamingActionTypeComboBox.SelectedIndex >= 0)
            {
                StreamingActionTypeEnum type = EnumHelper.GetEnumValueFromString<StreamingActionTypeEnum>((string)this.StreamingActionTypeComboBox.SelectedItem);
                if (type == StreamingActionTypeEnum.Scene)
                {
                    this.SceneGrid.Visibility = Visibility.Visible;
                }
                else if (type == StreamingActionTypeEnum.StartStopStream)
                {
                    // Do nothing...
                }
                else
                {
                    this.SourceGrid.Visibility = Visibility.Visible;
                    if (type == StreamingActionTypeEnum.TextSource)
                    {
                        this.SourceTextGrid.Visibility = Visibility.Visible;
                    }
                    else if (type == StreamingActionTypeEnum.WebBrowserSource)
                    {
                        this.SourceWebBrowserGrid.Visibility = Visibility.Visible;
                    }
                    else if (type == StreamingActionTypeEnum.SourceDimensions)
                    {
                        this.SourceDimensionsGrid.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void SourceLoadTextFromBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (!string.IsNullOrEmpty(this.SourceNameTextBox.Text)) ? this.SourceNameTextBox.Text + ".txt" : "Source.txt";
            if (this.action != null && !string.IsNullOrEmpty(this.action.SourceTextFilePath))
            {
                name = this.action.SourceTextFilePath;
            }
            string filePath = ChannelSession.Services.FileService.ShowSaveFileDialog(name);
            if (!string.IsNullOrEmpty(filePath))
            {
                this.SourceLoadTextFromTextBox.Text = filePath;
            }
        }

        private void SourceWebPageBrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string filePath = ChannelSession.Services.FileService.ShowOpenFileDialog();
            if (!string.IsNullOrEmpty(filePath))
            {
                this.SourceWebPageTextBox.Text = filePath;
            }
        }

        private async void GetSourcesDimensionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.StreamingSoftwareComboBox.SelectedIndex >= 0 && !string.IsNullOrEmpty(this.SourceNameTextBox.Text))
            {
                await this.containerControl.RunAsyncOperation(async () =>
                {
                    StreamingSourceDimensions dimensions = null;

                    StreamingSoftwareTypeEnum software = this.GetSelectedSoftware();
                    if (software == StreamingSoftwareTypeEnum.OBSStudio)
                    {
                        if (ChannelSession.Services.OBSWebsocket != null || await ChannelSession.Services.InitializeOBSWebsocket())
                        {
                            dimensions = ChannelSession.Services.OBSWebsocket.GetSourceDimensions(this.SourceNameTextBox.Text);
                        }
                        else
                        {
                            await MessageBoxHelper.ShowMessageDialog("Could not connect to OBS Studio. Please try establishing connection with it in the Services area.");
                        }
                    }
                    else if (software == StreamingSoftwareTypeEnum.StreamlabsOBS)
                    {
                        StreamlabsOBSScene activeScene = await ChannelSession.Services.StreamlabsOBSService.GetActiveScene();
                        if (activeScene != null)
                        {
                            IEnumerable<StreamlabsOBSSceneItem> sceneItems = await ChannelSession.Services.StreamlabsOBSService.GetSceneItems(activeScene);
                            StreamlabsOBSSceneItem selectedSceneItem = sceneItems.FirstOrDefault(s => s.Name.Equals(this.SourceNameTextBox.Text));
                            if (selectedSceneItem != null)
                            {
                                dimensions = new StreamingSourceDimensions()
                                {
                                    X = (int)selectedSceneItem.Transform.Position.X,
                                    Y = (int)selectedSceneItem.Transform.Position.Y,
                                    XScale = (int)selectedSceneItem.Transform.Scale.X,
                                    YScale = (int)selectedSceneItem.Transform.Scale.X,
                                    Rotation = (int)selectedSceneItem.Transform.Rotation
                                };
                            }
                        }
                    }

                    if (dimensions != null)
                    {
                        this.SourceDimensionsXPositionTextBox.Text = dimensions.X.ToString();
                        this.SourceDimensionsYPositionTextBox.Text = dimensions.Y.ToString();
                        this.SourceDimensionsRotationTextBox.Text = dimensions.Rotation.ToString();
                        this.SourceDimensionsXScaleTextBox.Text = dimensions.XScale.ToString();
                        this.SourceDimensionsYScaleTextBox.Text = dimensions.YScale.ToString();
                    }
                });
            }
        }

        private StreamingSoftwareTypeEnum GetSelectedSoftware()
        {
            StreamingSoftwareTypeEnum software = EnumHelper.GetEnumValueFromString<StreamingSoftwareTypeEnum>((string)this.StreamingSoftwareComboBox.SelectedItem);
            if (software == StreamingSoftwareTypeEnum.DefaultSetting)
            {
                software = ChannelSession.Settings.DefaultStreamingSoftware;
            }
            return software;
        }
    }
}