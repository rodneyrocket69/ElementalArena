using System.Collections.Generic;

public static class PieceDefinitions
{
    public static readonly Dictionary<string, Piece> BASE = new Dictionary<string, Piece>
    {
        // ── Attack ──────────────────────────────────────────────────────────
        ["FLARE"] = new Piece {
            key="FLARE", pieceName="Flare", cls="Attack", maxShards=2, move=4, dmg=1,
            // Passive: Scorch — enemies within 1 tile take 1 damage at end of player's turn
            ability=new AbilityDef { name="Inferno Burst", desc="Deal 1 damage to an enemy within 2 tiles.", range=2, cooldown=2, type=AbilityType.Damage }
        },
        ["VOLTIX"] = new Piece {
            key="VOLTIX", pieceName="Voltix", cls="Attack", maxShards=2, move=2, dmg=1,
            // Passive: Static Charge — charges earned per move, stacks to 3, resets to 1 on ANY damage
            ability=new AbilityDef { name="Shockwave", desc="Stun all enemies within radius (Static Charges × 1 tile). Resets charges.", range=0, cooldown=3, type=AbilityType.Shockwave }
        },
        ["ZEPHYROS"] = new Piece {
            key="ZEPHYROS", pieceName="Zephyros", cls="Attack", maxShards=2, move=3, dmg=1,
            // Passive: Windborn — immune to rooted status
            ability=new AbilityDef { name="Gale Slash", desc="Deal 1 damage in a straight line. Stops on first hit.", range=7, cooldown=3, type=AbilityType.Line }
        },

        // ── Defense ─────────────────────────────────────────────────────────
        ["FROSTBITE"] = new Piece {
            key="FROSTBITE", pieceName="Frostbite", cls="Defense", maxShards=4, move=2, dmg=1,
            // Passive: Ice Armor — 1 physical damage reduction (attacks do 0)
            ability=new AbilityDef { name="Glacial Impact", desc="Root all enemies in a 3×3 area around target tile (cast range 2).", range=2, cooldown=4, type=AbilityType.Freeze }
        },
        ["BULWARK"] = new Piece {
            key="BULWARK", pieceName="Bulwark", cls="Defense", maxShards=4, move=2, dmg=1,
            // Passive: Battle Hardened — immune to physical (attack) damage
            ability=new AbilityDef { name="Magnetic Fortress", desc="Redirect all attack damage aimed at allies within 2 tiles to Bulwark for 2 turns.", range=0, cooldown=3, type=AbilityType.Fortress }
        },
        ["AEGIS"] = new Piece {
            key="AEGIS", pieceName="Aegis", cls="Defense", maxShards=4, move=3, dmg=1,
            // Passive: Energy Shield — personal barrier blocks 1 hit; regenerates when any friendly kills an enemy
            ability=new AbilityDef { name="Barrier", desc="Shield an ally within 3 tiles for 2 turns. Absorbs one hit.", range=3, cooldown=3, type=AbilityType.Barrier }
        },

        // ── Support ─────────────────────────────────────────────────────────
        ["VERDANT"] = new Piece {
            key="VERDANT", pieceName="Verdant", cls="Support", maxShards=3, move=2, dmg=1,
            // Passive: Life Bloom — heal adjacent allies 1 shard at end of turn
            ability=new AbilityDef { name="Vine Snare", desc="Root all enemies in a 3×3 area around target tile (cast range 2).", range=2, cooldown=3, type=AbilityType.Freeze }
        },
        ["MIMIC"] = new Piece {
            key="MIMIC", pieceName="Mimic", cls="Support", maxShards=3, move=3, dmg=1,
            // Passive: Eerie Aura — enemies within 1 tile have move reduced by 1
            ability=new AbilityDef { name="Phantom Lantern", desc="Spawn a moveable phantom decoy on any adjacent empty tile.", range=1, cooldown=4, type=AbilityType.Decoy }
        },
        ["SHARDIS"] = new Piece {
            key="SHARDIS", pieceName="Shardis", cls="Support", maxShards=3, move=2, dmg=1,
            // Passive: Phased Form — immune to physical (attack) damage
            ability=new AbilityDef { name="Gravitic Distortion", desc="Pull all pieces within 3 tiles 1 step toward Shardis. Enemies weakened for 2 turns.", range=3, cooldown=5, type=AbilityType.Pull }
        },
    };

    public static Piece Make(string key, int player, string id)
    {
        var def = BASE[key];
        var p = def.Clone();
        p.id     = id;
        p.player = player;
        p.shards = def.maxShards;
        p.abilityCd = 0;
        p.isDecoy = false;
        p.staticCharges = key == "VOLTIX" ? 1 : 0;
        p.energyShieldActive = key == "AEGIS"; // Aegis starts with shield up
        return p;
    }

    public static Piece MakeDecoy(Piece owner)
    {
        return new Piece
        {
            key          = "DECOY",
            id           = owner.id + "_decoy",
            player       = owner.player,
            pieceName    = "Phantom",
            cls          = "Support",
            maxShards    = 1,
            shards       = 1,
            move         = owner.move, // decoy is moveable at full Mimic speed
            dmg          = 0,
            isDecoy      = true,
            decoyOwnerId = owner.id,
            ability      = new AbilityDef(),
        };
    }
}
