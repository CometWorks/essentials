"""PCU and ownership: pcu checkowner/checkauthor, pcucount, claim, the grid
sale (sellgrid, acceptgrid, denygrid), rename, and the transfer commands to a
player or to nobody."""

from __future__ import annotations

import re
import time

import pytest

import prefabs
import world
from conftest import wait_until
from session import FRONT, offset

A, B, C = world.PLAYER_A, world.PLAYER_B, world.PLAYER_C
CONFIRM = "Are you sure you want to continue? Enter the command again within 30 seconds to confirm."


def look(player, g: prefabs.Grid) -> None:
    target = g.top(dy=-g.cell_m / 2)
    player.stand_and_look(offset(target, dz=8, dy=1.5), target)


def mixed(name="Mixed", owner=B, share="None", cargo=True) -> prefabs.Grid:
    """Armor built by `owner`, a reactor and a battery of `owner`, and with
    `cargo` a cargo container of A"""
    g = prefabs.Grid(name, FRONT, slab=(3, 1, 3), owner=owner)
    g.add("reactor", (0, 1, 0), share=share)
    g.add("battery", (2, 1, 0), share=share)
    if cargo:
        g.add("cargo", (1, 1, 2), owner=A)
    return g


def owners(game, g: prefabs.Grid) -> dict[str, str]:
    return {kind: game.block(block_id)["owner"] for kind, block_id in g.blocks.items()}


def builders(game, g: prefabs.Grid) -> dict[str, str]:
    return {
        kind: game.block(block_id)["builtby"] for kind, block_id in g.blocks.items()
    }


# --- reports -----------------------------------------------------------------


def test_checkowner_and_checkauthor(game):
    g = mixed()
    game.spawn(g)
    assert game.a.ess("pcu checkowner Mixed").splitlines() == [
        "Owners at grid: Mixed",
        "   Bravo = 2 blocks",
        "   Alpha = 1 blocks",
    ]
    lines = game.a.ess("pcu checkauthor Mixed").splitlines()
    assert lines[0] == "Authors at grid: Mixed"
    assert re.fullmatch(r"   Bravo = \d+ PCU", lines[1]) and re.fullmatch(
        r"   Alpha = \d+ PCU", lines[2]
    ), lines
    look(game.a, g)
    assert game.a.ess("pcu checkowner").splitlines()[0] == "Owners at grid: Mixed"
    assert game.a.ess("pcu checkowner Zulu") == "Could not find the Grid."


def test_pcucount(game):
    from test_blocks import projector_xml

    g = mixed()
    blueprint = prefabs.Grid("Blueprint", (0.0, 0.0, 0.0), slab=(2, 1, 1))
    g.add("projector", (2, 1, 2), extra=projector_xml(blueprint))
    game.spawn(g)
    pcu = game.grid(g)["pcu"]
    look(game.b, g)
    lines = game.b.ess("pcucount").splitlines()
    assert lines[:2] == ["Grid PCU:", f"Mixed: {pcu:,}"]
    assert lines[-1] in (
        f"Total grid PCU: {pcu:,}",
        f"Total with projections: {pcu + 2:,}",
    ), lines


# --- claim -------------------------------------------------------------------


def test_claim_a_faction_grid(game):
    g = mixed(owner=C, share="Faction", cargo=False)
    game.spawn(g)
    look(game.b, g)
    assert game.b.ess("claim").startswith(
        "Claimed 1 grid(s). Accessible owned blocks: "
    )
    wait_until(lambda: owners(game, g)["reactor"] == str(B), 10, "B owning the reactor")


def test_claim_refusals(game):
    g = mixed(owner=C, share="None")
    game.spawn(g)
    look(game.a, g)
    assert game.a.ess("claim") == "Not in a faction."
    look(game.b, g)
    assert game.b.ess("claim").startswith("Not enough shared ownership on 'Mixed'.")
    assert owners(game, g)["reactor"] == str(C)


# --- sales -------------------------------------------------------------------


@pytest.fixture
def sale(game):
    """B's grid in front, A standing next to B, both able to pay"""
    g = prefabs.Grid("For Sale", FRONT, slab=(3, 1, 3), owner=B)
    g.add("reactor", (0, 1, 0))
    game.spawn(g)
    game.set_balance(A, 1000)
    game.set_balance(B, 0)
    look(game.b, g)
    game.a.teleport(offset(game.b.position(), dx=3))
    return g


def test_sell_and_accept(game, sale):
    a_before = game.a.mark()
    assert game.b.ess("sellgrid 600") == (
        "Offer to sell 1 grid(s) sent to Alpha for 600 credits. Offer is active for 30 seconds."
    )
    game.a.wait_line(
        a_before,
        lambda s, t: s == "Essentials"
        and t
        == "Bravo wants to sell you 1 grid(s) for 600 credits. Use !ess acceptgrid within 30 seconds.",
        what="the offer",
    )
    b_before = game.b.mark()
    assert (
        game.a.ess("acceptgrid")
        == "Accepted the offer. Transferred 1 physical grid group(s)."
    )
    game.b.wait_line(
        b_before,
        lambda s, t: t == "Player accepted your offer to sell grid.",
        what="the notice",
    )
    assert (game.balance(A), game.balance(B)) == (400, 600)
    wait_until(lambda: owners(game, sale)["reactor"] == str(A), 10, "A owning the grid")
    assert game.a.ess("acceptgrid") == "You have no offers available to accept."


def test_sell_and_deny(game, sale):
    game.b.ess("sellgrid 100")
    b_before = game.b.mark()
    assert game.a.ess("denygrid") == "Denied the offer."
    game.b.wait_line(
        b_before,
        lambda s, t: t == "Player denied your offer to sell grid.",
        what="the notice",
    )
    assert game.a.ess("denygrid") == "You have no offers available to deny."
    assert owners(game, sale)["reactor"] == str(B)


def test_sale_needs_the_money(game, sale):
    game.b.ess("sellgrid 5000")
    assert game.a.ess("acceptgrid") == "Sorry, but you are short 4,000 credits!"
    assert owners(game, sale)["reactor"] == str(B)


def test_offer_expires(game, sale):
    game.b.ess("sellgrid 10")
    time.sleep(31)
    assert game.a.ess("acceptgrid") == "You have no offers available to accept."


def test_one_offer_at_a_time(game, sale):
    game.b.ess("sellgrid 10")
    assert (
        game.b.ess("sellgrid 20")
        == "They already have an offer. Wait or have them use !ess denygrid."
    )


def test_sell_refusals(game, sale):
    assert game.b.ess("sellgrid lots") == "Error parsing into number."
    assert game.b.ess("sellgrid -5") == "No, the price cannot be below 0."
    # Well clear of the 10 m sphere: A's home, about 12 m from B, still counts
    game.a.teleport(offset(game.b.position(), dx=20))
    assert game.b.ess("sellgrid 10") == "No player within 10m to sell grid to."


# --- rename ------------------------------------------------------------------


def test_rename_own_grid(game):
    g = mixed(cargo=False)
    game.spawn(g)
    assert (
        game.b.ess("rename Mixed Bravo One")
        == "Renaming Mixed to Bravo One. You may need to relog to see changes."
    )
    wait_until(lambda: game.grid(g)["name"] == "Bravo_One", 10, "the new name")


def test_rename_refusals(game):
    game.spawn(mixed(owner=A, name="Alpha Grid"))
    assert game.b.ess('rename "Alpha Grid" Mine').startswith(
        "Not enough shared ownership on 'Alpha Grid'."
    )
    assert game.b.ess("rename Zulu Yankee") == "Could not find the Grid."
    assert (
        game.b.ess('rename "Alpha Grid"') == "Usage: !ess rename <gridName> <newName>"
    )


# --- transfers ---------------------------------------------------------------


def transfer(game, command: str) -> str:
    assert game.a.ess(command) == CONFIRM
    return game.a.ess(command)


@pytest.mark.parametrize(
    "verb, start, done, owner, builder",
    [
        (
            "transfer",
            "PCU and Ownership",
            "PCU and Ownership was transferred to Alpha!",
            A,
            A,
        ),
        (
            "forcetransfer",
            "PCU and Ownership",
            "PCU and Ownership was transferred to Alpha!",
            A,
            A,
        ),
        ("transferpcu", "PCU", "PCU was transferred to Alpha!", B, A),
        ("forcetransferpcu", "PCU", "PCU was transferred to Alpha!", B, A),
        ("transferowner", "Ownership", "Ownership was transferred to Alpha!", A, B),
    ],
)
def test_transfer_to_a_player(game, verb, start, done, owner, builder):
    g = mixed(cargo=False)
    game.spawn(g)
    assert transfer(game, f"{verb} Alpha Mixed").splitlines() == [
        f"Start transferring {start} to Alpha!",
        done,
    ]
    wait_until(
        lambda: owners(game, g) == {"reactor": str(owner), "battery": str(owner)},
        10,
        "the owners",
    )
    wait_until(
        lambda: builders(game, g) == {"reactor": str(builder), "battery": str(builder)},
        10,
        "the builders",
    )


@pytest.mark.parametrize(
    "verb, start, done, owner, builder",
    [
        ("transfernobody", "PCU and Ownership", "PCU and Ownership was removed!", 0, 0),
        ("transferpcunobody", "PCU", "PCU was removed!", B, 0),
        ("transferownernobody", "Ownership", "Ownership was removed!", 0, B),
    ],
)
def test_transfer_to_nobody(game, verb, start, done, owner, builder):
    g = mixed(cargo=False)
    game.spawn(g)
    assert transfer(game, f"{verb} Mixed").splitlines() == [
        f"Start transferring {start} to nobody!",
        done,
    ]
    wait_until(
        lambda: owners(game, g) == {"reactor": str(owner), "battery": str(owner)},
        10,
        "the owners",
    )
    wait_until(
        lambda: builders(game, g) == {"reactor": str(builder), "battery": str(builder)},
        10,
        "the builders",
    )


def test_transfer_by_look(game):
    g = mixed(cargo=False)
    game.spawn(g)
    look(game.a, g)
    transfer(game, "transferowner Alpha")
    wait_until(lambda: owners(game, g)["reactor"] == str(A), 10, "A owning it")


def test_transfer_bad_input(game):
    game.spawn(mixed())
    assert (
        game.a.ess("transfer")
        == "Correct Usage is !ess transfer <playerName> [gridName]"
    )
    assert game.a.ess("transfer Zulu Mixed") == "Player not Found!"
    assert game.a.ess("transfer Alpha Zulu") == "Could not find the Grid."
    assert game.a.ess("transfernobody Zulu") == "Could not find the Grid."
