﻿define(['data', 'knockout', 'knockout.punches'], function (data, ko, kop) {
    var viewModel = {
        gameId: ko.observable(),
        player: ko.observable(),
        players: ko.observableArray(),
        addToTeam: function () {
            $.connection.gameHub.server.addToTeam(viewModel.gameId(), viewModel.player().Text());
        },
        activate: function(game) {            
            viewModel.gameId(game.GameId());
            viewModel.players(game.AddToTeamPlayersSelectList());
        }
    };

    return viewModel;
});