﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Windows.Controls;
using Octgn.Site.Api;

namespace Octgn.Windows
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Input;

    using Microsoft.Win32;

    using Octgn.Annotations;
    using Octgn.Core;
    using Octgn.Core.DataManagers;
    using Octgn.Core.Util;
    using Octgn.DeckBuilder;
    using Octgn.Extentions;

    using agsXMPP;

    using Octgn.Controls;
    using Octgn.Library;
    using Octgn.Library.Exceptions;

    using Skylabs.Lobby;

    using log4net;

    /// <summary>
    /// Logic for Main
    /// </summary>
    public partial class Main : INotifyPropertyChanged
    {
        internal new static ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool showedSubscriptionMessageOnce = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Main"/> class.
        /// </summary>
        public Main()
        {
            this.InitializeComponent();
            this.TabControlMain.SelectionChanged += TabControlMainOnSelectionChanged;
            if (Program.IsReleaseTest)
            {
                this.Title = "OCTGN " + "[Test v" + Const.OctgnVersion + "]";
            }
            this.MatchmakingTab.IsEnabled = false;
            ConnectBox.Visibility = Visibility.Hidden;
            ConnectBoxProgressBar.IsIndeterminate = false;
            Program.LobbyClient.OnStateChanged += this.LobbyClientOnOnStateChanged;
            Program.LobbyClient.OnLoginComplete += this.LobbyClientOnOnLoginComplete;
            Program.LobbyClient.OnDisconnect += LobbyClientOnOnDisconnect;
            Program.LobbyClient.OnDataReceived += LobbyClient_OnDataReceived;
            this.PreviewKeyUp += this.OnPreviewKeyUp;
            this.Closing += this.OnClosing;
            //GameUpdater.Get().Start();
            this.Loaded += OnLoaded;
            ChatManager.Get().Start(ChatBar);
            this.Activated += OnActivated;
            //new GameFeedManager().CheckForUpdates();
        }

        private void TabControlMainOnSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
        {
            TabCustomGamesList.VisibleChanged(TabCustomGames.IsSelected);
        }

        private void OnActivated(object sender, EventArgs eventArgs)
        {
            this.StopFlashingWindow();
        }

        private void LobbyClientOnOnDisconnect(object sender, EventArgs eventArgs)
        {
            if (Program.LobbyClient.DisconnectedBecauseConnectionReplaced)
            {
                Program.DoCrazyException(new Exception("Disconnected because connection replaced"), "You have been disconnected because you logged in somewhere else. You'll have to exit and reopen OCTGN to reconnect.");
                return;
            }
            Program.LobbyClient.Stop();
            Program.LobbyClient.BeginReconnect();
            //TopMostMessageBox.Show(
            //    "You have been disconnected", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            this.Loaded -= OnLoaded;
            //SubscriptionModule.Get().IsSubbedChanged += Main_IsSubbedChanged;
            UpdateManager.Instance.Start();

            var uri = new System.Uri("/Resources/CustomDataAgreement.txt", UriKind.Relative);
            var info = Application.GetResourceStream(uri);
            var resource = "";
            using (var s = new StreamReader(info.Stream))
            {
                resource = s.ReadToEnd();
            }

            var hash = resource.Sha1().ToLowerInvariant();

            if (!hash.Equals(Prefs.CustomDataAgreementHash, StringComparison.InvariantCultureIgnoreCase))
            {
                Prefs.AcceptedCustomDataAgreement = false;
            }

            if (!Prefs.AcceptedCustomDataAgreement)
            {
                var result = TopMostMessageBox.Show(
                    resource + Environment.NewLine + Environment.NewLine + "By pressing 'Yes' you agree to the terms above. You must agree to the terms to use OCTGN.",
                    "OCTGN Custom Data Agreement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    Prefs.AcceptedCustomDataAgreement = true;
                    Prefs.CustomDataAgreementHash = hash.Clone() as string;
                    return;
                }
                else
                {
                    Program.Exit();
                }
            }
        }

        void Main_IsSubbedChanged(bool obj)
        {
            //Dispatcher.Invoke(
            //    new Action(() =>
            //              { this.menuSub.Visibility = obj == false ? Visibility.Visible : Visibility.Collapsed; }));
        }

        /// <summary>
        /// Gets or sets the connect message.
        /// </summary>
        private string ConnectMessage
        {
            get
            {
                var textboxText = string.Empty;
                Dispatcher.Invoke(new Action(() =>
                                                 {
                                                     textboxText = tbConnect.Content as string;
                                                     ConnectBox.Visibility = string.IsNullOrWhiteSpace(textboxText) ? Visibility.Hidden : Visibility.Visible;
                                                     ConnectBoxProgressBar.IsIndeterminate = string.IsNullOrWhiteSpace(textboxText);
                                                 }));
                return textboxText;
            }

            set
            {
                Dispatcher.BeginInvoke(new Action(() =>
                                                      {
                                                          tbConnect.Content = value;
                                                          ConnectBox.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Hidden : Visibility.Visible;
                                                          ConnectBoxProgressBar.IsIndeterminate = string.IsNullOrWhiteSpace(value);
                                                      }));
            }
        }

        /// <summary>
        /// Happens when the window is closing
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="cancelEventArgs">
        /// The cancel event args.
        /// </param>
        private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            if (WindowManager.PlayWindow != null)
            {
                if (!WindowManager.PlayWindow.TryClose())
                {
                    cancelEventArgs.Cancel = true;
                    return;
                }
                LobbyChat.Dispose();
            }
            //SubscriptionModule.Get().IsSubbedChanged -= this.Main_IsSubbedChanged;
            Program.LobbyClient.OnDisconnect -= LobbyClientOnOnDisconnect;
            Program.LobbyClient.Stop();
            //GameUpdater.Get().Stop();
            GameUpdater.Get().Dispose();
            Task.Factory.StartNew(Program.Exit);
        }

        /// <summary>
        /// The on preview key up.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="keyEventArgs">
        /// The key event arguments.
        /// </param>
        private void OnPreviewKeyUp(object sender, KeyEventArgs keyEventArgs)
        {
            switch (keyEventArgs.Key)
            {
                case Key.Escape:
                    ChatBar.HideChat();
                    break;
                case Key.F7:
                    if (X.Instance.Debug || Program.IsReleaseTest)
                        Program.LobbyClient.Disconnect();
                    break;
                case Key.F8:
                    {
                        if (X.Instance.Debug)
                        {
                            WindowManager.GrowlWindow.AddNotification(new GameInviteNotification(new InviteToGame { From = new User(new Jid("jim@of.octgn.net")) }, new HostedGameData { Name = "Chicken" }, GameManager.Get().Games.First()));
                        }
                        break;
                    }
            }
        }

        #region LobbyEvents

        /// <summary>
        /// The lobby client on on state changed.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="state">
        /// The state.
        /// </param>
        private void LobbyClientOnOnStateChanged(object sender, XmppConnectionState state)
        {
            this.ConnectMessage = state == XmppConnectionState.Disconnected ? string.Empty : state.ToString();
            if (state == XmppConnectionState.Disconnected)
            {
                this.SetStateOffline();
            }
        }

        /// <summary>
        /// The lobby client on on login complete.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="results">
        /// The results.
        /// </param>
        private void LobbyClientOnOnLoginComplete(object sender, LoginResults results)
        {
            this.Dispatcher.BeginInvoke(new Action(
                () =>
                {
                    ConnectBox.Visibility = Visibility.Hidden;
                    ConnectBoxProgressBar.IsIndeterminate = false;
                }));
            switch (results)
            {
                case LoginResults.Success:
                    this.SetStateOnline();
                    this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (GameManager.Get().GameCount == 0)
                                TabCustomGames.Focus();
                            else
                                TabCommunityChat.Focus();

                        })).Completed += (o, args) => Task.Factory.StartNew(() =>
                        {
                            Thread.Sleep(15000);
                            this.Dispatcher.Invoke(new Action(()
                                                              =>
                                {
                                    var s =
                                        SubscriptionModule.Get
                                            ().IsSubscribed;
                                    if (s != null && s == false)
                                    {
                                        if (showedSubscriptionMessageOnce == false)
                                        {
                                            ShowSubMessage();
                                            showedSubscriptionMessageOnce = true;
                                        }
                                    }
                                }));
                        });
                    break;
                default:
                    this.SetStateOffline();
                    break;
            }
        }

        void LobbyClient_OnDataReceived(object sender, DataRecType type, object data)
        {
            if (type == DataRecType.GameInvite)
            {
                if (Program.IsGameRunning) return;
                var idata = data as InviteToGame;
                Task.Factory.StartNew(() =>
                {
                    var hostedgame = new ApiClient().GetGameList().FirstOrDefault(x => x.Id == idata.SessionId);
                    var endTime = DateTime.Now.AddSeconds(15);
                    while (hostedgame == null && DateTime.Now < endTime)
                    {
                        hostedgame = new ApiClient().GetGameList().FirstOrDefault(x => x.Id == idata.SessionId);
                    }
                    if (hostedgame == null)
                    {
                        Log.WarnFormat(
                            "Tried to read game invite from {0}, but there was no matching running game", idata.From.UserName);
                        return;
                    }
                    var game = GameManager.Get().GetById(hostedgame.GameId);
                    if (game == null)
                    {
                        throw new UserMessageException("Game is not installed.");
                    }
                    WindowManager.GrowlWindow.AddNotification(new GameInviteNotification(idata, hostedgame, game));
                });
            }
        }

        #endregion

        #region Main States

        /// <summary>
        /// The set state offline.
        /// </summary>
        private void SetStateOffline()
        {
            this.Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
						this.MatchmakingTab.IsEnabled = false;
                        //TabCommunityChat.IsEnabled = false;
                        ProfileTab.IsEnabled = false;
                        //TabMain.Focus();
                        //menuSub.Visibility = Visibility.Collapsed;
                    }));
        }

        /// <summary>
        /// The set state online.
        /// </summary>
        private void SetStateOnline()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
						this.MatchmakingTab.IsEnabled = true;
                        TabCommunityChat.IsEnabled = true;
                        ProfileTab.IsEnabled = true;
                        ProfileTabContent.Load(Program.LobbyClient.Me);
                        //var subbed = SubscriptionModule.Get().IsSubscribed;
                        //if (subbed == null || subbed == false)
                        //    menuSub.Visibility = Visibility.Visible;
                        //else
                        //    menuSub.Visibility = Visibility.Collapsed;
                        if (Program.LobbyClient.Me.UserName.Contains(" "))
                            TopMostMessageBox.Show(
                                "WARNING: You have a space in your username. This will cause a host of problems on here. If you don't have a subscription, it would be best to make yourself a new account.",
                                "WARNING",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                    }));
        }

        #endregion

        /// <summary>
        /// The menu about clicked.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The e.
        /// </param>
        private void MenuAboutClick(object sender, RoutedEventArgs e)
        {
            var w = new AboutWindow();
            w.ShowDialog();
        }

        /// <summary>
        /// Exits the program. Fired from the menu item MenuExit
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The event arguments.
        /// </param>
        private void MenuExitClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MenuDeckEditorClick(object sender, RoutedEventArgs e)
        {
            if (GameManager.Get().GameCount == 0)
            {
                TopMostMessageBox.Show(
                    "You need to install a game before you can use the deck editor.",
                    "OCTGN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            if (WindowManager.DeckEditor == null)
            {
                WindowManager.DeckEditor = new DeckBuilderWindow();
                WindowManager.DeckEditor.Show();
            }
        }

        private void MenuOptionsClick(object sender, RoutedEventArgs e)
        {
            var options = new Options();
            options.ShowDialog();
        }

        private void MenuLogOffClick(object sender, RoutedEventArgs e)
        {
            Program.LobbyClient.LogOut();
        }

        private void MenuSubClick(object sender, RoutedEventArgs e)
        {
            this.ShowSubscribeSite(new SubType() { Description = "", Name = "" });
        }

        private void ShowSubscribeSite(SubType subtype)
        {
            Log.InfoFormat("Show sub site {0}", subtype);
            var url = SubscriptionModule.Get().GetSubscribeUrl(subtype);
            if (url != null)
            {
                Program.LaunchUrl(url);
            }
        }

        private void MenuHelpClick(object sender, RoutedEventArgs e)
        {
            Program.LaunchUrl(AppConfig.WebsitePath);
        }

        private void MenuSubBenefitsClick(object sender, RoutedEventArgs e)
        {
            ShowSubMessage();
        }

        public void ShowSubMessage()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(this.ShowSubMessage));
                return;
            }
            this.SubMessage.Visibility = Visibility.Visible;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void MenuDiagClick(object sender, RoutedEventArgs e)
        {
            Octgn.Windows.Diagnostics.Instance.Show();
        }

        private void MenuShareCurrentLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.CurrentLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.CurrentLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var res = TextUploader.Instance.UploadText(File.ReadAllText(Config.Instance.Paths.CurrentLogPath));

                Clipboard.SetText(res);

                this.LobbyChat.ChatInput.Text = res;
                TopMostMessageBox.Show(
                    "Your log file has been shared. The URL to your log file has been copied to your clipboard. You can press ctrl+v to paste it.");
            }
            catch (UserMessageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TopMostMessageBox.Show(
                    "Error " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MenuOpenCurrentLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.CurrentLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.CurrentLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                var process = new Process();
                process.StartInfo = new ProcessStartInfo()
                {
                    UseShellExecute = true,
                    FileName = Config.Instance.Paths.CurrentLogPath
                };

                process.Start();
            }
            catch (Exception ex)
            {
                Log.Warn("MenuOpenCurrentLogClick Error", ex);
            }
        }

        private void MenuSaveAsCurrentLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.CurrentLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.CurrentLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                var sfd = new SaveFileDialog();
                sfd.Title = "Save Log File To...";
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                sfd.FileName = "currentlog.txt";
                sfd.OverwritePrompt = true;
                if ((sfd.ShowDialog() ?? false))
                {
                    File.Copy(Config.Instance.Paths.CurrentLogPath, sfd.FileName, true);
                    //var str = File.ReadAllText(Config.Instance.Paths.CurrentLogPath);
                    //File.WriteAllText(sfd.FileName, str);
                }

            }
            catch (Exception ex)
            {
                TopMostMessageBox.Show(
                    "Error " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MenuSharePreviousLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.PreviousLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.PreviousLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var res = TextUploader.Instance.UploadText(File.ReadAllText(Config.Instance.Paths.PreviousLogPath));

                Clipboard.SetText(res);

                this.LobbyChat.ChatInput.Text = res;

                TopMostMessageBox.Show(
                    "Your log file has been shared. The URL to your log file has been copied to your clipboard. You can press ctrl+v to paste it.");
            }
            catch (UserMessageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TopMostMessageBox.Show(
                    "Error " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MenuOpenPreviousLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.PreviousLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.PreviousLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                var process = new Process();
                process.StartInfo = new ProcessStartInfo()
                {
                    UseShellExecute = true,
                    FileName = Config.Instance.Paths.PreviousLogPath
                };

                process.Start();
            }
            catch (Exception ex)
            {
                Log.Warn("MenuOpenPreviousLogClick Error", ex);
            }
        }

        private void MenuSaveAsPreviousLogClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(Config.Instance.Paths.CurrentLogPath))
                {
                    TopMostMessageBox.Show(
                        "Log file doesn't exist at " + Config.Instance.Paths.PreviousLogPath,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                var sfd = new SaveFileDialog();
                sfd.Title = "Save Log File To...";
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                sfd.FileName = "prevlog.txt";
                sfd.OverwritePrompt = true;
                if ((sfd.ShowDialog() ?? false))
                {
                    var str = File.ReadAllText(Config.Instance.Paths.PreviousLogPath);
                    File.WriteAllText(sfd.FileName, str);
                }

            }
            catch (Exception ex)
            {
                TopMostMessageBox.Show(
                    "Error " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MenuSourceCodeClick(object sender, RoutedEventArgs e)
        {
            Program.LaunchUrl("http://repo.octgn.net");
        }

        private void MenuPullRequestClick(object sender, RoutedEventArgs e)
        {
            Program.LaunchUrl("https://github.com/octgn/OCTGN/pulls?q=is%3Apr+is%3Aclosed");
        }

        private void MenuFeatureFundingClick(object sender, RoutedEventArgs e)
        {
            var win = new OctgnChrome();
            win.SizeToContent = SizeToContent.WidthAndHeight;
            win.CanResize = false;
            win.MinMaxButtonVisibility = Visibility.Hidden;
            win.MinimizeButtonVisibility = Visibility.Hidden;
            win.Content = new FeatureFundingMessage();
            win.Title = "Feature Funding";
            win.ShowDialog();
        }

        private void MenuAndroidAppClick(object sender, RoutedEventArgs e)
        {
            Program.LaunchUrl("https://play.google.com/store/apps/details?id=com.octgn.app");
        }
    }
}