﻿using Mixer.Base.Model.Chat;
using Mixer.Base.Model.User;
using MixItUp.Base.Commands;
using MixItUp.Base.Model;
using MixItUp.Base.Model.Chat.Mixer;
using MixItUp.Base.Model.Currency;
using MixItUp.Base.Model.User;
using MixItUp.Base.Services.Mixer;
using MixItUp.Base.Util;
using MixItUp.Base.ViewModel.Chat;
using MixItUp.Base.ViewModel.Chat.Mixer;
using MixItUp.Base.ViewModel.User;
using StreamingClient.Base.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MixItUp.Base.Services
{
    public interface IChatService
    {
        IMixerChatService MixerChatService { get; }

        Task Initialize(IMixerChatService mixerChatService);

        bool DisableChat { get; set; }

        ObservableCollection<ChatMessageViewModel> Messages { get; }

        Dictionary<string, MixrElixrEmoteModel> MixrElixrEmotes { get; }

        LockedDictionary<Guid, UserViewModel> AllUsers { get; }
        IEnumerable<UserViewModel> DisplayUsers { get; }
        event EventHandler DisplayUsersUpdated;

        event EventHandler ChatCommandsReprocessed;
        IEnumerable<ChatCommand> ChatMenuCommands { get; }

        event EventHandler<Dictionary<string, uint>> OnPollEndOccurred;

        Task SendMessage(string message, bool sendAsStreamer = false, StreamingPlatformTypeEnum platform = StreamingPlatformTypeEnum.All);
        Task Whisper(UserViewModel user, string message, bool sendAsStreamer = false, bool waitForResponse = false);
        Task Whisper(StreamingPlatformTypeEnum platform, string username, string message, bool sendAsStreamer = false, bool waitForResponse = false);

        Task DeleteMessage(ChatMessageViewModel message);
        Task ClearMessages();

        Task StartPoll(string question, IEnumerable<string> answers, uint lengthInSeconds);

        Task TimeoutUser(UserViewModel user, uint durationInSeconds);
        Task PurgeUser(UserViewModel user);

        Task ModUser(UserViewModel user);
        Task UnmodUser(UserViewModel user);

        Task BanUser(UserViewModel user);
        Task UnbanUser(UserViewModel user);

        void RebuildCommandTriggers();

        Task AddMessage(ChatMessageViewModel message);
    }

    public class ChatService : IChatService
    {
        private const string ChatEventLogDirectoryName = "ChatEventLogs";
        private const string ChatEventLogFileNameFormat = "ChatEventLog-{0}.txt";

        public IMixerChatService MixerChatService { get; private set; }

        public bool DisableChat { get; set; }

        public ObservableCollection<ChatMessageViewModel> Messages { get; private set; } = new ObservableCollection<ChatMessageViewModel>();
        private LockedDictionary<string, ChatMessageViewModel> messagesLookup = new LockedDictionary<string, ChatMessageViewModel>();

        public Dictionary<string, MixrElixrEmoteModel> MixrElixrEmotes { get; private set; } = new Dictionary<string, MixrElixrEmoteModel>();

        public LockedDictionary<Guid, UserViewModel> AllUsers { get; private set; } = new LockedDictionary<Guid, UserViewModel>();
        public IEnumerable<UserViewModel> DisplayUsers
        {
            get
            {
                lock (displayUsersLock)
                {
                    return this.displayUsers.Values.ToList().Take(ChannelSession.Settings.MaxUsersShownInChat);
                }
            }
        }
        public event EventHandler DisplayUsersUpdated = delegate { };
        private SortedList<string, UserViewModel> displayUsers = new SortedList<string, UserViewModel>();
        private object displayUsersLock = new object();

        public event EventHandler ChatCommandsReprocessed = delegate { };
        public IEnumerable<ChatCommand> ChatMenuCommands { get { return this.chatMenuCommands; } }
        private List<ChatCommand> chatMenuCommands = new List<ChatCommand>();

        public event EventHandler<Dictionary<string, uint>> OnPollEndOccurred = delegate { };

        private LockedList<PermissionsCommandBase> chatCommands = new LockedList<PermissionsCommandBase>();

        private HashSet<Guid> userEntranceCommands = new HashSet<Guid>();

        private SemaphoreSlim whisperNumberLock = new SemaphoreSlim(1);
        private Dictionary<Guid, int> whisperMap = new Dictionary<Guid, int>();

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private string currentChatEventLogFilePath;

        public ChatService() { }

        public async Task Initialize(IMixerChatService mixerChatService)
        {
            this.MixerChatService = mixerChatService;

            this.RebuildCommandTriggers();

            await ChannelSession.Services.FileService.CreateDirectory(ChatEventLogDirectoryName);
            this.currentChatEventLogFilePath = Path.Combine(ChatEventLogDirectoryName, string.Format(ChatEventLogFileNameFormat, DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)));

            this.MixerChatService.OnMessageOccurred += MixerChatService_OnMessageOccurred;
            this.MixerChatService.OnDeleteMessageOccurred += MixerChatService_OnDeleteMessageOccurred;
            this.MixerChatService.OnClearMessagesOccurred += MixerChatService_OnClearMessagesOccurred;
            this.MixerChatService.OnUsersJoinOccurred += MixerChatService_OnUsersJoinOccurred;
            this.MixerChatService.OnUserUpdateOccurred += MixerChatService_OnUserUpdateOccurred;
            this.MixerChatService.OnUsersLeaveOccurred += MixerChatService_OnUsersLeaveOccurred;
            this.MixerChatService.OnUserPurgeOccurred += MixerChatService_OnUserPurgeOccurred;
            this.MixerChatService.OnUserTimeoutOccurred += MixerChatService_OnUserTimeoutOccurred;
            this.MixerChatService.OnUserBanOccurred += MixerChatService_OnUserBanOccurred;

            if (ChannelSession.Settings.ShowMixrElixrEmotes)
            {
                IEnumerable<MixrElixrEmoteModel> emotes = await ChannelSession.Services.MixrElixr.GetChannelEmotes(ChannelSession.MixerChannel);
                if (emotes != null)
                {
                    foreach (MixrElixrEmoteModel emote in emotes)
                    {
                        this.MixrElixrEmotes[emote.code] = emote;
                    }
                }
            }

            await DispatcherHelper.InvokeDispatcher(async () =>
            {
                foreach (ChatMessageEventModel messageEvent in await this.MixerChatService.GetChatHistory(50))
                {
                    UserViewModel user = ChannelSession.Services.User.GetUserByMixerID(messageEvent.user_id);
                    MixerChatMessageViewModel message = new MixerChatMessageViewModel(messageEvent, user);
                    this.messagesLookup[message.ID] = message;
                    if (ChannelSession.Settings.LatestChatAtTop)
                    {
                        this.Messages.Insert(0, message);
                    }
                    else
                    {
                        this.Messages.Add(message);
                    }
                }
            });

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
            {
                await ChannelSession.MixerUserConnection.GetChatUsers(ChannelSession.MixerChannel, async (collection) =>
                {
                    List<UserViewModel> users = new List<UserViewModel>();
                    foreach (ChatUserModel chatUser in collection)
                    {
                        users.Add(await ChannelSession.Services.User.AddOrUpdateUser(chatUser));
                    }
                    await this.UsersJoined(users);
                }, int.MaxValue);
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            AsyncRunner.RunBackgroundTask(this.cancellationTokenSource.Token, 60000, this.ProcessHoursCurrency);
        }

        public async Task SendMessage(string message, bool sendAsStreamer = false, StreamingPlatformTypeEnum platform = StreamingPlatformTypeEnum.All)
        {
            if (platform.HasFlag(StreamingPlatformTypeEnum.Mixer))
            {
                await this.MixerChatService.SendMessage(message, sendAsStreamer);
            }
        }

        public async Task Whisper(UserViewModel user, string message, bool sendAsStreamer = false, bool waitForResponse = false) { await this.Whisper(user.Platform, user.Username, message, sendAsStreamer); }

        public async Task Whisper(StreamingPlatformTypeEnum platform, string username, string message, bool sendAsStreamer = false, bool waitForResponse = false)
        {
            if (platform.HasFlag(StreamingPlatformTypeEnum.Mixer))
            {
                if (waitForResponse)
                {
                    ChatMessageEventModel messageEvent = await this.MixerChatService.WhisperWithResponse(username, message, sendAsStreamer);
                    if (messageEvent != null)
                    {
                        await this.AddMessage(new MixerChatMessageViewModel(messageEvent, ChannelSession.GetCurrentUser()));
                    }
                }
                else
                {
                    await this.MixerChatService.Whisper(username, message, sendAsStreamer);
                }
            }
        }

        public async Task DeleteMessage(ChatMessageViewModel message)
        {
            if (message.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.DeleteMessage(message);
            }
        }

        public async Task ClearMessages()
        {
            await DispatcherHelper.InvokeDispatcher(() =>
            {
                this.messagesLookup.Clear();
                this.Messages.Clear();
                return Task.FromResult(0);
            });
            await this.MixerChatService.ClearMessages();
        }

        public async Task StartPoll(string question, IEnumerable<string> answers, uint lengthInSeconds)
        {
            StreamingPlatformTypeEnum platform = StreamingPlatformTypeEnum.Mixer;
            if (platform == StreamingPlatformTypeEnum.Mixer)
            {
                this.MixerChatService.OnPollEndOccurred += MixerChatService_OnPollEndOccurred;
                await this.MixerChatService.StartPoll(question, answers, lengthInSeconds);
            }
        }

        public async Task PurgeUser(UserViewModel user)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.PurgeUser(user.Username);
            }
        }

        public async Task TimeoutUser(UserViewModel user, uint durationInSeconds)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.TimeoutUser(user.Username, durationInSeconds);
            }
        }

        public async Task ModUser(UserViewModel user)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.ModUser(user);
            }
        }

        public async Task UnmodUser(UserViewModel user)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.UnmodUser(user);
            }
        }

        public async Task BanUser(UserViewModel user)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.BanUser(user);
            }
        }

        public async Task UnbanUser(UserViewModel user)
        {
            if (user.Platform == StreamingPlatformTypeEnum.Mixer)
            {
                await this.MixerChatService.UnbanUser(user);
            }
        }

        public void RebuildCommandTriggers()
        {
            this.chatCommands.Clear();
            this.chatMenuCommands.Clear();
            foreach (ChatCommand command in ChannelSession.Settings.ChatCommands.Where(c => c.IsEnabled))
            {
                this.chatCommands.Add(command);
                if (command.Requirements.Settings.ShowOnChatMenu)
                {
                    this.chatMenuCommands.Add(command);
                }
            }

            foreach (GameCommandBase command in ChannelSession.Settings.GameCommands.Where(c => c.IsEnabled))
            {
                this.chatCommands.Add(command);
            }

            foreach (PreMadeChatCommand command in ChannelSession.PreMadeChatCommands.Where(c => c.IsEnabled))
            {
                this.chatCommands.Add(command);
            }

            this.ChatCommandsReprocessed(this, new EventArgs());
        }

        public async Task AddMessage(ChatMessageViewModel message)
        {
            message.ProcessingStartTime = DateTimeOffset.Now;
            Logger.Log(LogLevel.Debug, string.Format("Message Received - {0} - {1} - {2}", message.ID.ToString(), message.ProcessingStartTime, message));

            // Pre message processing

            if (message is UserChatMessageViewModel)
            {
                if (message.Platform == StreamingPlatformTypeEnum.Mixer && message.User != null)
                {
                    UserViewModel activeUser = ChannelSession.Services.User.GetUserByMixerID(message.User.MixerID);
                    if (activeUser != null)
                    {
                        message.User = activeUser;
                    }
                }

                if (message.User != null)
                {
                    message.User.UpdateLastActivity();
                    if (message.IsWhisper && ChannelSession.Settings.TrackWhispererNumber && !message.IsStreamerOrBot && message.User.WhispererNumber == 0)
                    {
                        await this.whisperNumberLock.WaitAndRelease(() =>
                        {
                            if (!whisperMap.ContainsKey(message.User.ID))
                            {
                                whisperMap[message.User.ID] = whisperMap.Count + 1;
                            }
                            message.User.WhispererNumber = whisperMap[message.User.ID];
                            return Task.FromResult(0);
                        });
                    }
                }
            }

            // Add message to chat list
            if (!(message is AlertChatMessageViewModel) || !ChannelSession.Settings.OnlyShowAlertsInDashboard)
            {
                await DispatcherHelper.InvokeDispatcher(() =>
                {
                    this.messagesLookup[message.ID] = message;
                    if (ChannelSession.Settings.LatestChatAtTop)
                    {
                        this.Messages.Insert(0, message);
                    }
                    else
                    {
                        this.Messages.Add(message);
                    }

                    if (this.Messages.Count > ChannelSession.Settings.MaxMessagesInChat)
                    {
                        ChatMessageViewModel removedMessage = (ChannelSession.Settings.LatestChatAtTop) ? this.Messages.Last() : this.Messages.First();
                        this.messagesLookup.Remove(removedMessage.ID);
                        this.Messages.Remove(removedMessage);
                    }

                    return Task.FromResult(0);
                });
            }

            // Post message processing

            if (message is AlertChatMessageViewModel)
            {
                if (ChannelSession.Settings.WhisperAllAlerts)
                {
                    await ChannelSession.Services.Chat.Whisper(ChannelSession.GetCurrentUser(), message.PlainTextMessage, false);
                }
                GlobalEvents.AlertMessageReceived((AlertChatMessageViewModel)message);
            }
            else if (message is UserChatMessageViewModel)
            {
                if (message.IsWhisper)
                {
                    if (!string.IsNullOrEmpty(ChannelSession.Settings.NotificationChatWhisperSoundFilePath))
                    {
                        await ChannelSession.Services.AudioService.Play(ChannelSession.Settings.NotificationChatWhisperSoundFilePath, ChannelSession.Settings.NotificationChatWhisperSoundVolume);
                    }

                    // Don't send this if it's in response to another "You are whisperer #" message
                    if (ChannelSession.Settings.TrackWhispererNumber && message.User.WhispererNumber > 0 && !message.PlainTextMessage.StartsWith("You are whisperer #", StringComparison.InvariantCultureIgnoreCase))
                    {
                        await ChannelSession.Services.Chat.Whisper(message.User, $"You are whisperer #{message.User.WhispererNumber}.", false);
                    }
                }
                else
                {
                    if (this.DisableChat)
                    {
                        Logger.Log(LogLevel.Debug, string.Format("Deleting Message As Chat Disabled - {0} - {1}", message.ID, message));
                        await this.DeleteMessage(message);
                        return;
                    }

                    if (!string.IsNullOrEmpty(ChannelSession.Settings.NotificationChatTaggedSoundFilePath) && message.IsStreamerTagged)
                    {
                        await ChannelSession.Services.AudioService.Play(ChannelSession.Settings.NotificationChatTaggedSoundFilePath, ChannelSession.Settings.NotificationChatTaggedSoundVolume);
                    }
                    else if (!string.IsNullOrEmpty(ChannelSession.Settings.NotificationChatMessageSoundFilePath))
                    {
                        await ChannelSession.Services.AudioService.Play(ChannelSession.Settings.NotificationChatMessageSoundFilePath, ChannelSession.Settings.NotificationChatMessageSoundVolume);
                    }
                }

                await message.User.RefreshDetails();

                if (!message.IsWhisper && await message.CheckForModeration())
                {
                    await this.DeleteMessage(message);
                    return;
                }

                IEnumerable<string> arguments = null;
                if (ChannelSession.IsStreamer && !string.IsNullOrEmpty(message.PlainTextMessage) && message.User != null && !message.User.UserRoles.Contains(UserRoleEnum.Banned))
                {
                    if (!ChannelSession.Settings.AllowCommandWhispering && message.IsWhisper)
                    {
                        return;
                    }

                    if (ChannelSession.Settings.IgnoreBotAccountCommands && ChannelSession.MixerBot != null && message.User.MixerID.Equals(ChannelSession.MixerBot.id))
                    {
                        return;
                    }

                    if (ChannelSession.Settings.CommandsOnlyInYourStream && !message.IsInUsersChannel)
                    {
                        return;
                    }

                    Logger.Log(LogLevel.Debug, string.Format("Checking Message For Command - {0} - {1}", message.ID, message));

                    List<PermissionsCommandBase> commands = this.chatCommands.ToList();
                    foreach (PermissionsCommandBase command in message.User.Data.CustomCommands.Where(c => c.IsEnabled))
                    {
                        commands.Add(command);
                    }

                    foreach (PermissionsCommandBase command in commands)
                    {
                        if (command.DoesTextMatchCommand(message.PlainTextMessage, out arguments))
                        {
                            if (command.IsEnabled)
                            {
                                Logger.Log(LogLevel.Debug, string.Format("Command Found For Message - {0} - {1} - {2}", message.ID, message, command));
                                await command.Perform(message.User, message.Platform, arguments: arguments);
                                if (command.Requirements.Settings.DeleteChatCommandWhenRun || (ChannelSession.Settings.DeleteChatCommandsWhenRun && !command.Requirements.Settings.DontDeleteChatCommandWhenRun))
                                {
                                    await this.DeleteMessage(message);
                                }
                            }
                            break;
                        }
                    }
                }

                if (message.IsWhisper)
                {
                    if (!string.IsNullOrEmpty(message.PlainTextMessage))
                    {
                        EventTrigger trigger = new EventTrigger(EventTypeEnum.ChatWhisperReceived, message.User);
                        trigger.SpecialIdentifiers["message"] = message.PlainTextMessage;
                        await ChannelSession.Services.Events.PerformEvent(trigger);
                    }
                }
                else
                {
                    if (!this.userEntranceCommands.Contains(message.User.ID))
                    {
                        this.userEntranceCommands.Add(message.User.ID);
                        if (message.User.Data.EntranceCommand != null)
                        {
                            await message.User.Data.EntranceCommand.Perform(message.User, message.Platform);
                        }
                    }

                    if (!string.IsNullOrEmpty(message.PlainTextMessage))
                    {
                        EventTrigger trigger = new EventTrigger(EventTypeEnum.ChatMessageReceived, message.User);
                        trigger.SpecialIdentifiers["message"] = message.PlainTextMessage;
                        await ChannelSession.Services.Events.PerformEvent(trigger);
                    }

                    message.User.Data.TotalChatMessageSent++;

                    string primaryTaggedUsername = message.PrimaryTaggedUsername;
                    if (!string.IsNullOrEmpty(primaryTaggedUsername))
                    {
                        UserViewModel primaryTaggedUser = ChannelSession.Services.User.GetUserByUsername(primaryTaggedUsername, message.Platform);
                        if (primaryTaggedUser != null)
                        {
                            primaryTaggedUser.Data.TotalTimesTagged++;
                        }
                    }
                }

                foreach (InventoryModel inventory in ChannelSession.Settings.Inventory.Values.ToList())
                {
                    if (inventory.ShopEnabled && CommandBase.DoesTextMatchCommand(message.PlainTextMessage, CommandBase.CommandMatchingRegexFormat, new List<string>() { inventory.ShopCommand }, out arguments))
                    {
                        await inventory.PerformShopCommand(message.User, arguments, message.Platform);
                    }
                    else if (inventory.TradeEnabled && CommandBase.DoesTextMatchCommand(message.PlainTextMessage, CommandBase.CommandMatchingRegexFormat, new List<string>() { inventory.TradeCommand }, out arguments))
                    {
                        string args = message.PlainTextMessage.Replace(inventory.TradeCommand, "");
                        await inventory.PerformTradeCommand(message.User, arguments, message.Platform);
                    }
                }

                if (ChannelSession.Settings.RedemptionStoreEnabled)
                {
                    if (CommandBase.DoesTextMatchCommand(message.PlainTextMessage, CommandBase.CommandMatchingRegexFormat, new List<string>() { ChannelSession.Settings.RedemptionStoreChatPurchaseCommand }, out arguments))
                    {
                        await RedemptionStorePurchaseModel.Purchase(message.User, arguments);
                    }
                    else if (CommandBase.DoesTextMatchCommand(message.PlainTextMessage, CommandBase.CommandMatchingRegexFormat, new List<string>() { ChannelSession.Settings.RedemptionStoreModRedeemCommand }, out arguments))
                    {
                        await RedemptionStorePurchaseModel.Redeem(message.User, arguments);
                    }
                }

                GlobalEvents.ChatMessageReceived(message);
            }

            if (ChannelSession.Settings.SaveChatEventLogs)
            {
                try
                {
                    await ChannelSession.Services.FileService.AppendFile(this.currentChatEventLogFilePath, string.Format($"{message} ({DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)})" + Environment.NewLine));
                }
                catch (Exception) { }
            }

            Logger.Log(LogLevel.Debug, string.Format("Message Processing Complete: {0} - {1} ms", message.ID, message.ProcessingTime));
            if (message.ProcessingTime > 500)
            {
                Logger.Log(LogLevel.Error, string.Format("Long processing time detected for the following message: {0} - {1} ms - {2}", message.ID.ToString(), message.ProcessingTime, message));
            }
        }

        private async Task UsersJoined(IEnumerable<UserViewModel> users)
        {
            List<AlertChatMessageViewModel> alerts = new List<AlertChatMessageViewModel>();

            foreach (UserViewModel user in users)
            {
                this.AllUsers[user.ID] = user;
                lock (displayUsersLock)
                {
                    this.displayUsers[user.SortableID] = user;
                }

                if (ChannelSession.Settings.ChatShowUserJoinLeave && users.Count() < 5)
                {
                    alerts.Add(new AlertChatMessageViewModel(user.Platform, user, string.Format(MixItUp.Base.Resources.UserJoinedChat, user.Username), ChannelSession.Settings.ChatUserJoinLeaveColorScheme));
                }
            }
            this.DisplayUsersUpdated(this, new EventArgs());

            foreach (AlertChatMessageViewModel alert in alerts)
            {
                await this.AddMessage(alert);
            }
        }

        private async Task UsersUpdated(IEnumerable<UserViewModel> users)
        {
            await this.UsersLeft(users);
            await this.UsersJoined(users);
        }

        private async Task UsersLeft(IEnumerable<UserViewModel> users)
        {
            List<AlertChatMessageViewModel> alerts = new List<AlertChatMessageViewModel>();

            foreach (UserViewModel user in users)
            {
                if (this.AllUsers.Remove(user.ID))
                {
                    lock (displayUsersLock)
                    {
                        if (!this.displayUsers.Remove(user.SortableID))
                        {
                            int index = this.displayUsers.IndexOfValue(user);
                            if (index >= 0)
                            {
                                this.displayUsers.RemoveAt(index);
                            }
                        }
                    }

                    if (ChannelSession.Settings.ChatShowUserJoinLeave && users.Count() < 5)
                    {
                        alerts.Add(new AlertChatMessageViewModel(user.Platform, user, string.Format(MixItUp.Base.Resources.UserLeftChat, user.Username), ChannelSession.Settings.ChatUserJoinLeaveColorScheme));
                    }
                }
            }
            this.DisplayUsersUpdated(this, new EventArgs());

            foreach (AlertChatMessageViewModel alert in alerts)
            {
                await this.AddMessage(alert);
            }
        }

        private Task ProcessHoursCurrency(CancellationToken cancellationToken)
        {
            foreach (UserViewModel user in ChannelSession.Services.User.GetAllWorkableUsers())
            {
                user.UpdateMinuteData();
            }

            foreach (CurrencyModel currency in ChannelSession.Settings.Currency.Values)
            {
                currency.UpdateUserData();
            }

            foreach (StreamPassModel streamPass in ChannelSession.Settings.StreamPass.Values)
            {
                streamPass.UpdateUserData();
            }

            return Task.FromResult(0);
        }

        #region Mixer Events

        private async void MixerChatService_OnMessageOccurred(object sender, ChatMessageViewModel message)
        {
            await this.AddMessage(message);
        }

        private async void MixerChatService_OnDeleteMessageOccurred(object sender, Tuple<Guid, UserViewModel> messageDeletion)
        {
            if (this.messagesLookup.TryGetValue(messageDeletion.Item1.ToString(), out ChatMessageViewModel message))
            {
                await message.Delete(messageDeletion.Item2);
                GlobalEvents.ChatMessageDeleted(messageDeletion.Item1);

                if (ChannelSession.Settings.HideDeletedMessages)
                {
                    await DispatcherHelper.InvokeDispatcher(() =>
                    {
                        this.messagesLookup.Remove(messageDeletion.Item1.ToString());
                        this.Messages.Remove(message);
                        return Task.FromResult(0);
                    });
                }
            }
        }

        private async void MixerChatService_OnClearMessagesOccurred(object sender, EventArgs e)
        {
            await DispatcherHelper.InvokeDispatcher(() =>
            {
                this.messagesLookup.Clear();
                this.Messages.Clear();
                return Task.FromResult(0);
            });
        }

        private async void MixerChatService_OnUsersJoinOccurred(object sender, IEnumerable<UserViewModel> users)
        {
            await this.UsersJoined(users);
        }

        private async void MixerChatService_OnUserUpdateOccurred(object sender, UserViewModel user)
        {
            await this.UsersUpdated(new List<UserViewModel>() { user });
        }

        private async void MixerChatService_OnUsersLeaveOccurred(object sender, IEnumerable<UserViewModel> users)
        {
            await this.UsersLeft(users);
        }

        private async void MixerChatService_OnUserPurgeOccurred(object sender, MixerChatUserModerationModel e)
        {
            string reason = EnumLocalizationHelper.GetLocalizedName(e.Type);
            if (!string.IsNullOrEmpty(e.Length))
            {
                reason += $" {e.Length}";
            }

            foreach (ChatMessageViewModel message in this.Messages.ToList())
            {
                if (message.Platform == StreamingPlatformTypeEnum.Mixer && message.User != null && message.User.Equals(e.User))
                {
                    await message.Delete(moderator: e.Moderator, reason: reason);
                }
            }

            EventTrigger trigger = (e.Moderator != null) ? new EventTrigger(EventTypeEnum.ChatUserPurge, e.Moderator) : new EventTrigger(EventTypeEnum.ChatUserPurge);
            trigger.Arguments.Add(e.User.Username);
            await ChannelSession.Services.Events.PerformEvent(trigger);
        }

        private async void MixerChatService_OnUserTimeoutOccurred(object sender, MixerChatUserModerationModel e)
        {
            EventTrigger trigger = (e.Moderator != null) ? new EventTrigger(EventTypeEnum.ChatUserTimeout, e.Moderator) : new EventTrigger(EventTypeEnum.ChatUserTimeout);
            trigger.Arguments.Add(e.User.Username);
            trigger.SpecialIdentifiers["timeoutlength"] = e.Length.ToString();
            await ChannelSession.Services.Events.PerformEvent(trigger);
        }

        private async void MixerChatService_OnUserBanOccurred(object sender, MixerChatUserModerationModel e)
        {
            EventTrigger trigger = (e.Moderator != null) ? new EventTrigger(EventTypeEnum.ChatUserBan, e.Moderator) : new EventTrigger(EventTypeEnum.ChatUserBan);
            trigger.Arguments.Add(e.User.Username);
            await ChannelSession.Services.Events.PerformEvent(trigger);

            if (ChannelSession.Settings.ChatShowEventAlerts)
            {
                await ChannelSession.Services.Chat.AddMessage(new AlertChatMessageViewModel(StreamingPlatformTypeEnum.Mixer, e.User,
                    string.Format("{0} Banned By {1}", e.User.Username, (e.Moderator != null) ? e.Moderator.Username : "Unknown"),
                    ChannelSession.Settings.ChatEventAlertsColorScheme));
            }
        }

        private void MixerChatService_OnPollEndOccurred(object sender, ChatPollEventModel pollResults)
        {
            this.MixerChatService.OnPollEndOccurred -= MixerChatService_OnPollEndOccurred;

            Dictionary<string, uint> results = new Dictionary<string, uint>();
            foreach (string answer in pollResults.answers)
            {
                results[answer] = 0;
                if (pollResults.responses.ContainsKey(answer))
                {
                    results[answer] = pollResults.responses[answer].ToObject<uint>();
                }
            }

            this.OnPollEndOccurred(this, results);
        }

        #endregion Mixer Events
    }
}
