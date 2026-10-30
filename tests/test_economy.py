"""Economy commands: the native `econ` verbs, the `eco` aliases with faction
accounts, balance resets, physical SpaceCredit deposits and withdrawals, and
the account refresh and debug commands. Balances are read and set on the
server through the probe."""

from __future__ import annotations

import re
import time

import pytest

import prefabs
import world
from session import FRONT, NO_PERMISSION, offset

A, B, C = world.PLAYER_A, world.PLAYER_B, world.PLAYER_C


@pytest.fixture(autouse=True)
def balances(game):
    """Known balances for the players and the faction. The Empty World's own
    player, Skinface, gets none, so it stays out of the top lists."""
    balances = (
        (A, 5000),
        (B, 2000),
        (C, 1000),
        (world.SKINFACE, 0),
        (world.FACTION, 3000),
    )
    for owner, amount in balances:
        game.set_balance(owner, amount)


# --- econ --------------------------------------------------------------------


def test_econ_give_take_set_reset(game):
    assert game.a.ess("econ give Bravo 500") == "500 credits given to 1 account(s)"
    assert game.balance(B) == 2500
    assert game.a.ess("econ take Bravo 1500") == "1,500 credits taken from 1 account(s)"
    assert game.balance(B) == 1000
    # Taking more than there is leaves zero
    game.a.ess("econ take Bravo 99999")
    assert game.balance(B) == 0
    assert game.a.ess("econ set Bravo 1234") == "Balance(s) set to 1,234 on 1 accounts"
    assert game.balance(B) == 1234
    assert game.a.ess("econ reset Bravo") == "Balance(s) set to 10,000 on 1 accounts"
    assert game.balance(B) == 10000


def test_econ_by_identity_and_steam_id(game):
    game.a.ess(f"econ give {B} 1")
    game.a.ess(f"econ give {game.b.steam_id} 1")
    assert game.balance(B) == 2002


def test_econ_everyone_and_online_only(game):
    reply = game.a.ess("econ give * 10")
    assert re.fullmatch(r"10 credits given to \d+ account\(s\)", reply), reply
    assert (game.balance(A), game.balance(B), game.balance(C)) == (5010, 2010, 1010)
    assert game.a.ess("econ give * 5 true") == "5 credits given to 2 account(s)"
    assert (game.balance(A), game.balance(B), game.balance(C)) == (5015, 2015, 1010)
    assert game.a.ess("econ give Charlie 5 true") == "5 credits given to 0 account(s)"


def test_econ_unknown_player(game):
    for verb in ("give Zulu 1", "take Zulu 1", "set Zulu 1", "reset Zulu"):
        assert game.a.ess(f"econ {verb}") == "Player cannot be found!", verb
    assert game.b.ess("econ check Zulu") == "Player cannot be found!"


@pytest.mark.xfail(
    strict=True, reason="econ give and take take negative amounts, SE1-0245"
)
def test_econ_refuses_negative_amounts(game):
    # Checked after each command: a give of -500 and a take of -500 cancel out
    game.a.ess("econ give Bravo -500")
    assert game.balance(B) == 2000
    game.a.ess("econ take Bravo -500")
    assert game.balance(B) == 2000


def test_econ_check_and_top(game):
    assert game.b.ess("econ check Alpha") == "Alpha's balance is 5,000 credits"
    lines = game.b.ess("econ top").splitlines()
    assert lines[0] == "Summary of balances across the server"
    names = [
        re.match(r"Player: (.+) - Balance: ([\d,]+)", line).groups()
        for line in lines[1:]
    ]
    ours = [(n, b) for n, b in names if n in ("Alpha", "Bravo", "Charlie")]
    assert ours == [("Alpha", "5,000"), ("Bravo", "2,000"), ("Charlie", "1,000")]
    online = game.b.ess("econ top true").splitlines()
    assert online[1:] == [
        "Player: Alpha - Balance: 5,000",
        "Player: Bravo - Balance: 2,000",
    ]


def test_econ_pay(game):
    assert game.b.ess("econ pay Alpha 750") == "Sent 750 credits to Alpha."
    assert (game.balance(A), game.balance(B)) == (5750, 1250)


def test_econ_pay_reaches_clients(game):
    """A client that missed the payment still holds 5,000 for A. It drops the connection
    on a debit bigger than that, and takes the next command with it."""
    assert game.b.ess("econ pay Alpha 750") == "Sent 750 credits to Alpha."
    assert game.a.probe("changebalance", A, -5500) == "Probe changebalance: True"
    time.sleep(3)
    assert game.a.ess("econ check Alpha") == "Alpha's balance is 250 credits"
    assert game.b.ess("econ check Bravo") == "Bravo's balance is 1,250 credits"


def test_econ_pay_refusals(game):
    assert (
        game.b.ess("econ pay Alpha 5000") == "Sorry, but you are short 3,000 credits!"
    )
    assert game.b.ess("econ pay Bravo 5") == "You cannot pay yourself!"
    assert game.b.ess("econ pay Alpha 0") == "Amount cannot be negative"
    assert (
        game.b.ess("econ pay Charlie 5") == "Player is not online or cannot be found!"
    )
    assert (game.balance(A), game.balance(B)) == (5000, 2000)


# --- eco aliases -------------------------------------------------------------


def test_eco_help(game):
    lines = game.b.ess("eco").splitlines()
    assert lines[0] == "Economy commands:"
    assert lines[-1] == "Native aliases: !ess econ give/take/set/reset/top/check/pay"


def test_eco_balance(game):
    assert game.a.ess("eco balance player Bravo") == "Bravo's balance is 2,000 credits"
    assert (
        game.a.ess(f"eco balance faction {world.FACTION_TAG}")
        == f"{world.FACTION_NAME} Faction Balance : 3,000"
    )
    assert game.a.ess("eco balance faction NOPE") == "Cant find that faction"
    assert game.a.ess("eco balance ship Bravo").startswith("Incorrect usage, example:")


def test_eco_top(game):
    lines = game.a.ess("eco top 2").splitlines()
    assert lines == [
        "Top 2 player balances",
        "Player: Alpha - Balance: 5,000",
        "Player: Bravo - Balance: 2,000",
    ]
    factions = game.a.ess("eco top 30 true").splitlines()
    assert factions[0] == "Top 30 faction balances"
    assert f"{world.FACTION_NAME} - {world.FACTION_TAG} - Balance: 3,000" in factions
    assert game.a.ess("eco top 0") == "Limit must be greater than 0."


def test_eco_give_and_take(game):
    assert (
        game.a.ess("eco give player Bravo 1,000")
        == "1,000 credits given to 1 account(s)"
    )
    assert game.balance(B) == 3000
    assert (
        game.a.ess("eco take player Bravo 500") == "500 credits taken from 1 account(s)"
    )
    assert game.balance(B) == 2500
    assert game.a.ess(f"eco give faction {world.FACTION_TAG} 500").splitlines() == [
        f"{world.FACTION_NAME} FACTION Balance Before Change : 3,000",
        f"{world.FACTION_NAME} FACTION Balance After Change : 3,500",
    ]
    assert game.a.ess(f"eco take faction {world.FACTION_TAG} 9000").splitlines() == [
        "They cant afford that.",
        f"{world.FACTION_NAME} Current Balance : 3,500",
    ]
    assert game.balance(world.FACTION) == 3500


def test_eco_aliases_for_players_and_factions(game):
    assert game.a.ess("eco giveplayer Bravo 100") == "100 credits given to 1 account(s)"
    assert game.a.ess("eco takeplayer Bravo 50") == "50 credits taken from 1 account(s)"
    assert game.balance(B) == 2050
    game.a.ess(f"eco givefac {world.FACTION_TAG} 100")
    game.a.ess(f"eco takefac {world.FACTION_TAG} 50")
    assert game.balance(world.FACTION) == 3050


def test_eco_bad_amounts(game):
    assert game.a.ess("eco give player Bravo lots") == "Error parsing into number"
    assert game.a.ess("eco give player Bravo -5") == "Amount must be positive."
    assert game.a.ess("eco giveplayer Bravo 0") == "Amount must be positive."
    assert (
        game.a.ess(f"eco givefac {world.FACTION_TAG} 0") == "Amount must be positive."
    )
    assert game.a.ess("eco give ship Bravo 5").startswith("Incorrect usage, example:")
    assert game.a.ess("eco give faction NOPE 5") == "Cant find that faction"
    assert game.balance(B) == 2000


def test_eco_pay(game):
    assert game.b.ess("eco pay player Alpha 100") == "Sent 100 credits to Alpha."
    assert (
        game.b.ess(f"eco pay faction {world.FACTION_TAG} 100")
        == f"Sent 100 credits to {world.FACTION_NAME}."
    )
    assert (game.balance(A), game.balance(B), game.balance(world.FACTION)) == (
        5100,
        1800,
        3100,
    )
    # An offline player can be paid through eco
    assert game.b.ess("eco pay player Charlie 100") == "Sent 100 credits to Charlie."
    assert game.b.ess("eco pay player Bravo 1") == "You cannot pay yourself!"
    assert game.b.ess("eco pay player Zulu 1") == "Cant find that player"


def test_eco_resets(game):
    reply = game.a.ess("eco resetplayer Bravo")
    assert reply == "Bravo balance before change: 2,000\nBravo balance after change: 0"
    assert game.a.ess(f"eco resetfac {world.FACTION_TAG}") == (
        f"{world.FACTION_NAME} faction balance before change: 3,000\n{world.FACTION_NAME} faction balance after change: 0"
    )
    assert game.a.ess("eco resetplayer Zulu") == "Could not find that player."
    assert game.a.ess("eco resetfac NOPE") == "Error faction not found."


def test_eco_reset_all(game):
    assert re.fullmatch(
        r"\d+ player balance\(s\) reset\.", game.a.ess("eco resetplayers")
    )
    start = game.balance(A)
    assert game.balance(B) == game.balance(C) == start
    assert re.fullmatch(
        r"\d+ faction balance\(s\) reset\.", game.a.ess("eco resetfactions")
    )
    assert game.balance(world.FACTION) == 0
    game.set_balance(B, 1)
    game.set_balance(world.FACTION, 1)
    assert re.fullmatch(r"\d+ balance\(s\) reset\.", game.a.ess("eco resetbalances"))
    assert game.balance(B) == start and game.balance(world.FACTION) == 0


# --- SpaceCredit items -------------------------------------------------------


@pytest.fixture
def vault(game):
    g = prefabs.Grid("Vault", FRONT, slab=(3, 1, 3), owner=B)
    cargo = g.add("cargo", (1, 1, 1))
    game.spawn(g)
    target = g.top(dy=-g.cell_m / 2)
    game.b.stand_and_look(offset(target, dz=10, dy=1.5), target)
    return g, cargo


def test_withdraw_and_deposit(game, vault):
    g, cargo = vault
    reply = game.b.ess("eco withdraw 1500")
    assert re.fullmatch(r"Added credits to .+ in grid Vault\.", reply), reply
    assert game.balance(B) == 500
    assert game.inventory(cargo) == {"PhysicalObject/SpaceCredit": 1500.0}
    assert game.b.ess("eco deposit") == "Deposited: 1,500"
    assert game.balance(B) == 2000
    assert game.inventory(cargo) == {}


def test_withdraw_refusals(game, vault):
    assert game.b.ess("eco withdraw 5000") == "You do not have that much money."
    assert game.b.ess("eco withdraw 0") == "Amount must be positive."
    assert (
        game.b.ess("eco withdraw 3000000000")
        == "Keen code does not allow stacks over 2.147 billion; try a smaller number."
    )
    assert game.b.ess("eco deposit") == "No SpaceCredit items found."
    assert game.b.ess("eco withdrawall") == NO_PERMISSION
    assert game.a.ess("eco withdrawall") == "Command disabled. Withdraw with an amount."


def test_withdraw_into_someone_elses_grid(game):
    g = prefabs.Grid("Alpha Vault", FRONT, slab=(3, 1, 3), owner=A)
    g.add("cargo", (1, 1, 1))
    game.spawn(g)
    target = g.top(dy=-g.cell_m / 2)
    game.b.stand_and_look(offset(target, dz=10, dy=1.5), target)
    assert (
        game.b.ess("eco withdraw 100")
        == "No accessible cargo container has free space for that many credits."
    )
    assert game.balance(B) == 2000


# --- account refresh and debug -----------------------------------------------


def test_sync_and_debug(game):
    assert game.a.ess("TestEconSync") == "Refreshed your balance account."
    assert re.fullmatch(
        r"Refreshed \d+ local balance account\(s\)\.", game.a.ess("FullEconSync")
    )
    assert re.fullmatch(
        r"Refreshed \d+ local balance account\(s\)\.", game.a.ess("fulleconsync")
    )
    assert re.fullmatch(
        r"Refreshed \d+ local balance account\(s\)\.", game.a.ess("econ sync all")
    )
    assert game.a.ess("SingleEconSync Bravo") == "Refreshed balance account for Bravo."
    assert game.a.ess("singleeconsync Bravo") == "Refreshed balance account for Bravo."
    assert (
        game.a.ess("econ sync player Bravo") == "Refreshed balance account for Bravo."
    )
    assert game.a.ess("econ sync player Zulu") == "Could not find that player."
    # Refreshing keeps the balances
    assert (game.balance(A), game.balance(B)) == (5000, 2000)
    assert game.a.ess("econ debug account Bravo") == f"Bravo ({B}) balance: 2,000"
    assert (
        game.a.ess(f"econ debug account {world.FACTION_TAG}")
        == f"{world.FACTION_NAME} ({world.FACTION}) balance: 3,000"
    )
    assert (
        game.a.ess("econ debug account Zulu")
        == "Could not find that player or faction."
    )
