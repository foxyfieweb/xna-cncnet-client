﻿using ClientCore;
using ClientCore.CnCNet5;
using ClientGUI;
using DTAClient.Domain.LAN;
using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.LAN;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer.GameLobby;
using DTAClient.Online;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DTAClient.DXGUI.Multiplayer
{
    class LANLobby : XNAWindow
    {
        private const double ALIVE_MESSAGE_INTERVAL = 5.0;
        private const double INACTIVITY_REMOVE_TIME = 10.0;
        private const double GAME_INACTIVITY_REMOVE_TIME = 20.0;

        public LANLobby(WindowManager windowManager, GameCollection gameCollection,
            List<GameMode> gameModes)
            : base(windowManager)
        {
            this.gameCollection = gameCollection;
            this.gameModes = gameModes;
        }

        public event EventHandler Exited;

        XNAListBox lbPlayerList;
        ChatListBox lbChatMessages;
        GameListBox lbGameList;

        XNAClientButton btnMainMenu;
        XNAClientButton btnNewGame;
        XNAClientButton btnJoinGame;

        XNATextBox tbChatInput;

        XNALabel lblColor;

        XNAClientDropDown ddColor;

        LANGameCreationWindow gameCreationWindow;

        LANGameLobby lanGameLobby;

        LANGameLoadingLobby lanGameLoadingLobby;

        Texture2D unknownGameIcon;

        LANColor[] chatColors;

        string localGame;
        int localGameIndex;

        GameCollection gameCollection;

        List<GameMode> gameModes;

        TimeSpan timeSinceGameRefresh = TimeSpan.Zero;

        ToggleableSound sndGameCreated;

        Socket socket;
        IPEndPoint endPoint;
        Encoding encoding;

        List<LANLobbyUser> players = new List<LANLobbyUser>();

        TimeSpan timeSinceAliveMessage = TimeSpan.Zero;

        bool initSuccess = false;

        public override void Initialize()
        {
            Name = "LANLobby";
            BackgroundTexture = AssetLoader.LoadTexture("cncnetlobbybg.png");
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX - 64,
                WindowManager.RenderResolutionY - 64);

            localGame = ClientConfiguration.Instance.LocalGame;
            localGameIndex = gameCollection.GameList.FindIndex(
                g => g.InternalName.ToUpper() == localGame.ToUpper());

            btnNewGame = new XNAClientButton(WindowManager);
            btnNewGame.Name = "btnNewGame";
            btnNewGame.ClientRectangle = new Rectangle(12, ClientRectangle.Height - 35, 133, 23);
            btnNewGame.Text = "Create Game";
            btnNewGame.LeftClick += BtnNewGame_LeftClick;

            btnJoinGame = new XNAClientButton(WindowManager);
            btnJoinGame.Name = "btnJoinGame";
            btnJoinGame.ClientRectangle = new Rectangle(btnNewGame.ClientRectangle.Right + 12,
                btnNewGame.ClientRectangle.Y, 133, 23);
            btnJoinGame.Text = "Join Game";
            btnJoinGame.LeftClick += BtnJoinGame_LeftClick;

            btnMainMenu = new XNAClientButton(WindowManager);
            btnMainMenu.Name = "btnMainMenu";
            btnMainMenu.ClientRectangle = new Rectangle(ClientRectangle.Width - 145,
                btnNewGame.ClientRectangle.Y, 133, 23);
            btnMainMenu.Text = "Main Menu";
            btnMainMenu.LeftClick += BtnMainMenu_LeftClick;

            lbGameList = new GameListBox(WindowManager, localGame);
            lbGameList.Name = "lbGameList";
            lbGameList.ClientRectangle = new Rectangle(btnNewGame.ClientRectangle.X,
                41, btnJoinGame.ClientRectangle.Right - btnNewGame.ClientRectangle.X,
                btnNewGame.ClientRectangle.Top - 53);
            lbGameList.GameLifetime = 15.0; // Smaller lifetime in LAN
            lbGameList.DrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbGameList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbGameList.DoubleLeftClick += LbGameList_DoubleLeftClick;
            lbGameList.AllowMultiLineItems = false;

            lbPlayerList = new XNAListBox(WindowManager);
            lbPlayerList.Name = "lbPlayerList";
            lbPlayerList.ClientRectangle = new Rectangle(ClientRectangle.Width - 202,
                lbGameList.ClientRectangle.Y, 190,
                lbGameList.ClientRectangle.Height);
            lbPlayerList.DrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbPlayerList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbPlayerList.LineHeight = 16;

            lbChatMessages = new ChatListBox(WindowManager);
            lbChatMessages.Name = "lbChatMessages";
            lbChatMessages.ClientRectangle = new Rectangle(lbGameList.ClientRectangle.Right + 12,
                lbGameList.ClientRectangle.Y,
                lbPlayerList.ClientRectangle.Left - lbGameList.ClientRectangle.Right - 24,
                lbGameList.ClientRectangle.Height);
            lbChatMessages.DrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbChatMessages.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbChatMessages.LineHeight = 16;

            tbChatInput = new XNATextBox(WindowManager);
            tbChatInput.Name = "tbChatInput";
            tbChatInput.ClientRectangle = new Rectangle(lbChatMessages.ClientRectangle.X,
                btnNewGame.ClientRectangle.Y, lbChatMessages.ClientRectangle.Width,
                btnNewGame.ClientRectangle.Height);
            tbChatInput.MaximumTextLength = 200;
            tbChatInput.EnterPressed += TbChatInput_EnterPressed;

            lblColor = new XNALabel(WindowManager);
            lblColor.Name = "lblColor";
            lblColor.ClientRectangle = new Rectangle(lbChatMessages.ClientRectangle.X, 14, 0, 0);
            lblColor.FontIndex = 1;
            lblColor.Text = "YOUR COLOR:";

            ddColor = new XNAClientDropDown(WindowManager);
            ddColor.Name = "ddColor";
            ddColor.ClientRectangle = new Rectangle(lblColor.ClientRectangle.X + 95, 12,
                150, 21);

            chatColors = new LANColor[]
            {
                new LANColor("Gray", Color.Gray),
                new LANColor("Metalic", Color.LightGray),
                new LANColor("Green", Color.Green),
                new LANColor("Lime Green", Color.LimeGreen),
                new LANColor("Green Yellow", Color.GreenYellow),
                new LANColor("Goldenrod", Color.Goldenrod),
                new LANColor("Yellow", Color.Yellow),
                new LANColor("Orange", Color.Orange),
                new LANColor("Red", Color.Red),
                new LANColor("Pink", Color.DeepPink),
                new LANColor("Purple", Color.MediumPurple),
                new LANColor("Sky Blue", Color.SkyBlue),
                new LANColor("Blue", Color.Blue),
                new LANColor("Brown", Color.SaddleBrown),
                new LANColor("Teal", Color.Teal)
            };

            foreach (LANColor color in chatColors)
            {
                ddColor.AddItem(color.Name, color.XNAColor);
            }

            AddChild(btnNewGame);
            AddChild(btnJoinGame);
            AddChild(btnMainMenu);

            AddChild(lbPlayerList);
            AddChild(lbChatMessages);
            AddChild(lbGameList);
            AddChild(tbChatInput);
            AddChild(lblColor);
            AddChild(ddColor);

            gameCreationWindow = new LANGameCreationWindow(WindowManager);
            var gameCreationPanel = new DarkeningPanel(WindowManager);
            AddChild(gameCreationPanel);
            gameCreationPanel.AddChild(gameCreationWindow);
            gameCreationWindow.Disable();

            gameCreationWindow.NewGame += GameCreationWindow_NewGame;
            gameCreationWindow.LoadGame += GameCreationWindow_LoadGame;

            unknownGameIcon = AssetLoader.TextureFromImage(ClientCore.Properties.Resources.unknownicon);

            SoundEffect gameCreatedSoundEffect = AssetLoader.LoadSound("gamecreated.wav");

            if (gameCreatedSoundEffect != null)
                sndGameCreated = new ToggleableSound(gameCreatedSoundEffect.CreateInstance());

            encoding = Encoding.UTF8;

            base.Initialize();

            CenterOnParent();
            gameCreationPanel.SetPositionAndSize();

            lanGameLobby = new LANGameLobby(WindowManager, "MultiplayerGameLobby",
                null, gameModes, chatColors);
            DarkeningPanel.AddAndInitializeWithControl(WindowManager, lanGameLobby);
            lanGameLobby.Disable();

            lanGameLoadingLobby = new LANGameLoadingLobby(WindowManager, 
                gameModes, chatColors);
            DarkeningPanel.AddAndInitializeWithControl(WindowManager, lanGameLoadingLobby);
            lanGameLoadingLobby.Disable();

            int selectedColor = UserINISettings.Instance.LANChatColor;

            ddColor.SelectedIndex = selectedColor >= ddColor.Items.Count || selectedColor < 0
                ? 0 : selectedColor;

            SetChatColor();
            ddColor.SelectedIndexChanged += DdColor_SelectedIndexChanged;

            lanGameLobby.GameLeft += LanGameLobby_GameLeft;
            lanGameLobby.GameBroadcast += LanGameLobby_GameBroadcast;

            lanGameLoadingLobby.GameBroadcast += LanGameLoadingLobby_GameBroadcast;
            lanGameLoadingLobby.GameLeft += LanGameLoadingLobby_GameLeft;

            WindowManager.GameClosing += WindowManager_GameClosing;

            lbChatMessages.AddMessage(null, "Please note that LAN game support is currently work-in-progress. " +
                "While basic functionality should work, it is possible that you'll encounter various kinds of bugs and possibly even crashes. Please report all issues to the client lead developer (Rampastring) at " +
                "http://www.moddb.com/members/rampastring so we can fix the issues for future builds.", Color.Yellow);
        }

        private void LanGameLoadingLobby_GameLeft(object sender, EventArgs e)
        {
            Enable();
        }

        private void WindowManager_GameClosing(object sender, EventArgs e)
        {
            if (socket == null)
                return;

            if (socket.IsBound)
            {
                try
                {
                    SendMessage("QUIT");
                    socket.Close();
                }
                catch (ObjectDisposedException)
                {

                }
            }
        }

        private void LanGameLobby_GameBroadcast(object sender, GameBroadcastEventArgs e)
        {
            SendMessage(e.Message);
        }

        private void LanGameLobby_GameLeft(object sender, EventArgs e)
        {
            Enable();
        }

        private void LanGameLoadingLobby_GameBroadcast(object sender, GameBroadcastEventArgs e)
        {
            SendMessage(e.Message);
        }

        private void GameCreationWindow_LoadGame(object sender, GameLoadEventArgs e)
        {
            lanGameLoadingLobby.SetUp(true,
                new IPEndPoint(IPAddress.Loopback, ProgramConstants.LAN_GAME_LOBBY_PORT),
                null, e.LoadedGameID);

            lanGameLoadingLobby.Enable();
        }

        private void GameCreationWindow_NewGame(object sender, EventArgs e)
        {
            lanGameLobby.SetUp(true, 
                new IPEndPoint(IPAddress.Loopback, ProgramConstants.LAN_GAME_LOBBY_PORT), null);

            lanGameLobby.Enable();
        }

        private void SetChatColor()
        {
            tbChatInput.TextColor = chatColors[ddColor.SelectedIndex].XNAColor;
            lanGameLobby.SetChatColorIndex(ddColor.SelectedIndex);
            UserINISettings.Instance.LANChatColor.Value = ddColor.SelectedIndex;
        }

        private void DdColor_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetChatColor();
            UserINISettings.Instance.SaveSettings();
        }

        public void Open()
        {
            players.Clear();
            lbPlayerList.Clear();
            lbGameList.ClearGames();

            Visible = true;
            Enabled = true;

            Logger.Log("Creating LAN socket.");

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.EnableBroadcast = true;
                socket.Bind(new IPEndPoint(IPAddress.Any, ProgramConstants.LAN_LOBBY_PORT));
                endPoint = new IPEndPoint(IPAddress.Broadcast, ProgramConstants.LAN_LOBBY_PORT);
                initSuccess = true;
            }
            catch (Exception ex)
            {
                Logger.Log("Creating LAN socket failed! Message: " + ex.Message);
                lbChatMessages.AddMessage(new ChatMessage(null, Color.Red, DateTime.Now,
                    "Creating LAN socket failed! Message: " + ex.Message));
                lbChatMessages.AddMessage(new ChatMessage(null, Color.Red, DateTime.Now,
                    "Please check your firewall settings."));
                lbChatMessages.AddMessage(new ChatMessage(null, Color.Red, DateTime.Now,
                    "Also make sure that no other application is listening to traffic on UDP ports 1232 - 1234."));
                initSuccess = false;
                return;
            }

            Logger.Log("Starting listener.");
            new Thread(new ThreadStart(Listen)).Start();

            SendAlive();
        }

        private void SendMessage(string message)
        {
            if (!initSuccess)
                return;

            byte[] buffer;

            buffer = encoding.GetBytes(message);

            socket.SendTo(buffer, endPoint);
        }

        private void Listen()
        {
            try
            {
                while (true)
                {
                    EndPoint ep = new IPEndPoint(IPAddress.Any, ProgramConstants.LAN_LOBBY_PORT);
                    byte[] buffer = new byte[4096];
                    int receivedBytes = 0;
                    receivedBytes = socket.ReceiveFrom(buffer, ref ep);

                    IPEndPoint iep = (IPEndPoint)ep;

                    string data = encoding.GetString(buffer, 0, receivedBytes);

                    if (data == string.Empty)
                        continue;

                    AddCallback(new Action<string, IPEndPoint>(HandleNetworkMessage), data, iep);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("LAN socket listener: exception: " + ex.Message);
            }
        }

        private void HandleNetworkMessage(string data, IPEndPoint endPoint)
        {
            string[] commandAndParams = data.Split(' ');

            if (commandAndParams.Length < 2)
                return;

            string command = commandAndParams[0];

            string[] parameters = data.Substring(command.Length + 1).Split(
                new char[] { ProgramConstants.LAN_DATA_SEPARATOR },
                StringSplitOptions.RemoveEmptyEntries);

            LANLobbyUser user = players.Find(p => p.EndPoint.Equals(endPoint));

            switch (command)
            {
                case "ALIVE":
                    if (parameters.Length < 2)
                        return;

                    int gameIndex = Conversions.IntFromString(parameters[0], -1);
                    string name = parameters[1];

                    if (user == null)
                    {
                        Texture2D gameTexture = unknownGameIcon;

                        if (gameIndex > -1 && gameIndex < gameCollection.GameList.Count)
                            gameTexture = gameCollection.GameList[gameIndex].Texture;

                        user = new LANLobbyUser(name, gameTexture, endPoint);
                        players.Add(user);
                        lbPlayerList.AddItem(user.Name, gameTexture);
                    }

                    user.TimeWithoutRefresh = TimeSpan.Zero;

                    break;
                case "CHAT":
                    if (user == null)
                        return;

                    if (parameters.Length < 2)
                        return;

                    int colorIndex = Conversions.IntFromString(parameters[0], -1);

                    if (colorIndex < 0 || colorIndex >= chatColors.Length)
                        return;

                    lbChatMessages.AddMessage(new ChatMessage(user.Name, 
                        chatColors[colorIndex].XNAColor, DateTime.Now, parameters[1]));

                    break;
                case "QUIT":
                    if (user == null)
                        return;

                    int index = players.FindIndex(p => p == user);

                    players.RemoveAt(index);
                    lbPlayerList.Items.RemoveAt(index);
                    break;
                case "GAME":
                    if (user == null)
                        return;

                    HostedLANGame game = new HostedLANGame();
                    if (!game.SetDataFromStringArray(gameCollection, parameters))
                        return;
                    game.EndPoint = endPoint;

                    int existingGameIndex = lbGameList.HostedGames.FindIndex(g => ((HostedLANGame)g).EndPoint.Equals(endPoint));

                    if (existingGameIndex > -1)
                        lbGameList.HostedGames[existingGameIndex] = game;
                    else
                    {
                        lbGameList.HostedGames.Add(game);
                    }

                    lbGameList.Refresh();

                    break;
            }
        }

        private void SendAlive()
        {
            StringBuilder sb = new StringBuilder("ALIVE ");
            sb.Append(localGameIndex);
            sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
            sb.Append(ProgramConstants.PLAYERNAME);
            SendMessage(sb.ToString());
            timeSinceAliveMessage = TimeSpan.Zero;
        }

        private void TbChatInput_EnterPressed(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(tbChatInput.Text))
                return;

            string chatMessage = tbChatInput.Text.Replace((char)01, '?');

            StringBuilder sb = new StringBuilder("CHAT ");
            sb.Append(ddColor.SelectedIndex);
            sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
            sb.Append(chatMessage);

            SendMessage(sb.ToString());

            tbChatInput.Text = string.Empty;
        }

        private void LbGameList_DoubleLeftClick(object sender, EventArgs e)
        {
            if (lbGameList.SelectedIndex < 0 || lbGameList.SelectedIndex >= lbGameList.Items.Count)
                return;

            HostedLANGame hg = (HostedLANGame)lbGameList.Items[lbGameList.SelectedIndex].Tag;

            if (hg.Game.InternalName.ToUpper() != localGame.ToUpper())
            {
                lbChatMessages.AddMessage(new ChatMessage(null, Color.White, DateTime.Now,
                    "The selected game is for " +
                    gameCollection.GetGameNameFromInternalName(hg.Game.InternalName) + "!"));
                return;
            }

            if (hg.Locked)
            {
                lbChatMessages.AddMessage(null, "The selected game is locked!", Color.White);
                return;
            }

            if (hg.IsLoadedGame)
            {
                if (!hg.Players.Contains(ProgramConstants.PLAYERNAME))
                {
                    lbChatMessages.AddMessage(null, "You do not exist in the saved game!", Color.White);
                    return;
                }
            }
            else
            {
                if (hg.Players.Contains(ProgramConstants.PLAYERNAME))
                {
                    lbChatMessages.AddMessage(null, "Your name is already taken in the game.", Color.White);
                    return;
                }
            }

            if (hg.GameVersion != ProgramConstants.GAME_VERSION)
            {
                // TODO Show warning
            }

            lbChatMessages.AddMessage(new ChatMessage(null, Color.White, DateTime.Now,
                "Attempting to join game " + hg.RoomName + "..."));

            try
            {
                var client = new TcpClient(hg.EndPoint.Address.ToString(), ProgramConstants.LAN_GAME_LOBBY_PORT);

                byte[] buffer;

                if (hg.IsLoadedGame)
                {
                    var spawnSGIni = new IniFile(ProgramConstants.GamePath +
                        ProgramConstants.SAVED_GAME_SPAWN_INI);

                    int loadedGameId = spawnSGIni.GetIntValue("Settings", "GameID", -1);

                    lanGameLoadingLobby.SetUp(false, hg.EndPoint, client, loadedGameId);
                    lanGameLoadingLobby.Enable();

                    buffer = encoding.GetBytes("JOIN" + ProgramConstants.LAN_DATA_SEPARATOR +
                        ProgramConstants.PLAYERNAME + ProgramConstants.LAN_DATA_SEPARATOR +
                        loadedGameId + ProgramConstants.LAN_MESSAGE_SEPARATOR);

                    client.GetStream().Write(buffer, 0, buffer.Length);
                    client.GetStream().Flush();

                    lanGameLoadingLobby.PostJoin();
                }
                else
                {
                    lanGameLobby.SetUp(false, hg.EndPoint, client);
                    lanGameLobby.Enable();

                    buffer = encoding.GetBytes("JOIN" + ProgramConstants.LAN_DATA_SEPARATOR + 
                        ProgramConstants.PLAYERNAME + ProgramConstants.LAN_MESSAGE_SEPARATOR);

                    client.GetStream().Write(buffer, 0, buffer.Length);
                    client.GetStream().Flush();

                    lanGameLobby.PostJoin();
                }
            }
            catch (Exception ex)
            {
                lbChatMessages.AddMessage(null,
                    "Connecting to the game failed! Message: " + ex.Message, Color.White);
            }
        }

        private void BtnMainMenu_LeftClick(object sender, EventArgs e)
        {
            Visible = false;
            Enabled = false;
            SendMessage("QUIT");
            socket.Close();
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void BtnJoinGame_LeftClick(object sender, EventArgs e)
        {
            LbGameList_DoubleLeftClick(this, EventArgs.Empty);
        }

        private void BtnNewGame_LeftClick(object sender, EventArgs e)
        {
            if (!ClientConfiguration.Instance.DisableMultiplayerGameLoading) gameCreationWindow.Open();
            else GameCreationWindow_NewGame(sender, e);
        }

        public override void Update(GameTime gameTime)
        {
            for (int i = 0; i < players.Count; i++)
            {
                players[i].TimeWithoutRefresh += gameTime.ElapsedGameTime;

                if (players[i].TimeWithoutRefresh > TimeSpan.FromSeconds(INACTIVITY_REMOVE_TIME))
                {
                    lbPlayerList.Items.RemoveAt(i);
                    players.RemoveAt(i);
                    i--;
                }
            }

            timeSinceAliveMessage += gameTime.ElapsedGameTime;
            if (timeSinceAliveMessage > TimeSpan.FromSeconds(ALIVE_MESSAGE_INTERVAL))
                SendAlive();

            base.Update(gameTime);
        }
    }
}
