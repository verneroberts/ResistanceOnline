﻿using Microsoft.AspNet.SignalR;
using ResistanceOnline.Core;
using ResistanceOnline.Database;
using ResistanceOnline.Site.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using System.Web.Mvc;
using ResistanceOnline.Database.Entities;
using Action = ResistanceOnline.Core.Action;
using Game = ResistanceOnline.Core.Game;
using Rule = ResistanceOnline.Core.Rule;
using Character = ResistanceOnline.Core.Character;


namespace ResistanceOnline.Site.Controllers
{
    //[Authorize]
    public class GameHub : Hub
    {
        readonly ResistanceOnlineDbContext _dbContext;
        public GameHub()//(ResistanceOnlineDbContext dbContext)
        {
            _dbContext = new Database.ResistanceOnlineDbContext(); //todo injection
            
            //create a default game to make development easier
            if (Games.Count == 0)
            {
                var game = new Game();
                game.Rules.Clear();
                game.Rules.Add(Rule.LadyOfTheLakeExists);

                game.JoinGame("\"Jordan\"", Guid.NewGuid(), Core.Player.Type.TrustBot);
                game.JoinGame("\"Luke\"", Guid.NewGuid(), Core.Player.Type.TrustBot);
                game.JoinGame("\"Jeffrey\"", Guid.NewGuid(), Core.Player.Type.TrustBot);
                game.JoinGame("\"Jayvin\"", Guid.NewGuid(), Core.Player.Type.TrustBot);
                game.JoinGame("\"Yif\"", Guid.NewGuid(), Core.Player.Type.TrustBot);
                game.JoinGame("\"Alex\"", Guid.NewGuid(), Core.Player.Type.TrustBot);

                game.AvailableCharacters[0] = Character.Merlin;
                game.AvailableCharacters[1] = Character.Assassin;
                game.AvailableCharacters[2] = Character.Percival;
                game.AvailableCharacters[3] = Character.Morgana;
                game.AvailableCharacters[4] = Character.Mordred;
                game.AvailableCharacters[5] = Character.LoyalServantOfArthur;

                Games.Add(game);
                game.GameId = Games.IndexOf(game);
            }
        }

        //todo logged in user
        private Guid PlayerGuid
        {
            get
            {
                return CurrentUser.PlayerGuid;
            }
        }

        private UserAccount CurrentUser
        {
            get
            {
                UserAccount userAccount = null;
                if (Context.User != null)
                {
                    var userId = Context.User.Identity.GetUserId();
                    userAccount = _dbContext.Users.FirstOrDefault(user => user.Id == userId);
                }
                if (userAccount == null)
                {
                    userAccount = new UserAccount { PlayerGuid = Guid.Empty };
                }
                return userAccount;
            }
        }


        public static List<Game> Games = new List<Game>();
        public static List<Action> _actions = new List<Action>();
        static Dictionary<Guid, List<string>> _userConnections = new Dictionary<Guid, List<string>>();

        private void AddAction(int gameId, Action action)
        {
            action.GameId = gameId;

            var actionDb = new Database.Entities.Action
            {
                Game = _dbContext.Games.First(x=>x.GameId == gameId),
                Owner = _dbContext.Players.First(x=>x.Game.GameId == gameId && x.Guid == action.Owner.Guid),
                Timestamp = action.Timestamp,
                Type = action.ActionType.ToString(),
                Text = action.Text
            };
            if (action.TargetPlayer!=null) {
                actionDb.Target = _dbContext.Players.FirstOrDefault(x => x.Game.GameId == gameId && x.Guid == action.TargetPlayer.Guid);
            }

            _dbContext.Actions.Add(actionDb);
            _dbContext.SaveChanges();
        }

        private GamePlay GetGamePlay(int? gameId)
        {
            //todo - something to do with databases
            if (gameId.HasValue == false || gameId.Value >= Games.Count)
                return null;

            var gameplay = new GamePlay(Games[gameId.Value]);
            gameplay.DoActions(_actions.Where(a => a.GameId == gameId).ToList());
            return gameplay;
        }

        private void Update()
        {
            foreach (var guid in _userConnections.Keys)
            {
                //todo - don't need all games sent every update
                //it feels like this should be split out into game hubs for playing games and home page stuff for managing games
                var games = Games.Select(g => new GamePlayModel(GetGamePlay(g.GameId), guid));

                foreach (var connection in _userConnections[guid])
                {
                    Clients.Client(connection).Update(games);
                }
            }
        }

        public override System.Threading.Tasks.Task OnConnected()
        {
            //todo cleanup on disconnection
            if (PlayerGuid != Guid.Empty)
            {
                if (!_userConnections.ContainsKey(PlayerGuid))
                {
                    _userConnections[PlayerGuid] = new List<string>();
                }
                _userConnections[PlayerGuid].Add(Context.ConnectionId);
            }
            Update();
            return base.OnConnected();
        }

        public GamePlay CreateGame()
        {
            //todo - something with the database :)
            var game = new Game();
            Games.Add(game);
            game.GameId = Games.IndexOf(game);

            Update();

            return new GamePlay(game);
        }

        private void DoAction(int gameId, Action.Type actionType, string targetPlayerName = null, string text = null)
        {
            var game = GetGamePlay(gameId);
            var owner = game.Game.Players.FirstOrDefault(p => p.Guid == PlayerGuid);
            if (owner == null)
            {
                throw new UnauthorizedAccessException("Player needs to learn to play their own games");
            }
            var targetPlayer = game.Game.Players.FirstOrDefault(p => p.Name == targetPlayerName);
            var action = new Action(owner, actionType, targetPlayer, text);
            game.DoAction(action);
            AddAction(gameId, action);
            LetComputerPlayersDoActions(game);
        }

        private void LetComputerPlayersDoActions(GamePlay gameplay)
        {
            var state = gameplay.GamePlayState;
            var computersPlayersInGame = gameplay.Game.Players.Where(p=>p.PlayerType != Core.Player.Type.Human);
            while (computersPlayersInGame.Any(c => gameplay.AvailableActions(gameplay.Game.Players.First(p => p.Guid == c.Guid)).Any(action => action != Action.Type.Message)))
            {
                foreach (var computerPlayer in computersPlayersInGame)
                {                    
                    var action = Core.ComputerPlayers.ComputerPlayer.Factory(computerPlayer.PlayerType, computerPlayer.Guid).DoSomething(gameplay);
                    if (action != null)
                    {
                        gameplay.DoAction(action);
                        AddAction(gameplay.Game.GameId, action);
                    }
                }
            }
        }

        public void AddToTeam(int gameId, string person)
        {
            DoAction(gameId, Action.Type.AddToTeam, targetPlayerName: person);
            Update();
        }

        public void SucceedQuest(int gameId)
        {
            DoAction(gameId, Action.Type.SucceedQuest);
            Update();
        }

        public void FailQuest(int gameId)
        {
            DoAction(gameId, Action.Type.FailQuest);
            Update();
        }

        public void VoteApprove(int gameId)
        {
            DoAction(gameId, Action.Type.VoteApprove);
            Update();
        }

        public void VoteReject(int gameId)
        {
            DoAction(gameId, Action.Type.VoteReject);
            Update();
        }

        public void GuessMerlin(int gameId, string guess)
        {
            DoAction(gameId, Action.Type.GuessMerlin, targetPlayerName:guess);
            Update();
        }

        public void LadyOfTheLake(int gameId, string target)
        {
            DoAction(gameId, Action.Type.UseTheLadyOfTheLake, targetPlayerName: target);
            Update();
        }


        public void Message(int gameId, string message)
        {
            DoAction(gameId, Action.Type.Message, text: message);
            Update();
        }

        public void AssignExcalibur(int gameId, string proposedPlayerName)
        {
            DoAction(gameId, Action.Type.AssignExcalibur, targetPlayerName:proposedPlayerName);
            Update();
        }

        public void UseExcalibur(int gameId, string proposedPlayerName)
        {
            DoAction(gameId, Action.Type.UseExcalibur, targetPlayerName: proposedPlayerName);
            Update();
        }
    }
}