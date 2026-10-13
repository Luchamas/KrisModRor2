# Kris

Play as **Kris** from the hit videogame DELTARUNE!

Sword, shield, SOUL and ACT, all running on TP.

## Skills

| | Skill | Description |
| :---: | --- | --- |
| ![TENSION](https://raw.githubusercontent.com/Luchamas/KrisModRor2/main/Build/readme-icons/Passive.png) | **TENSION**<br>*Passive* | Being near enemies and landing hits builds **TP**, up to 100%. Your SOUL and your ACTs spend it. |
| ![FIGHT](https://raw.githubusercontent.com/Luchamas/KrisModRor2/main/Build/readme-icons/Primary.png) | **FIGHT**<br>*Primary* | Slash with your sword for **250%** damage. Hold to keep swinging. |
| ![DEFEND](https://raw.githubusercontent.com/Luchamas/KrisModRor2/main/Build/readme-icons/Secondary.png) | **DEFEND**<br>*Secondary* | Hold to raise your shield and take **90%** less damage. Every hit you take behind it grants **5% TP**. You stand your ground: no moving, jumping or attacking. |
| ![SOUL](https://raw.githubusercontent.com/Luchamas/KrisModRor2/main/Build/readme-icons/Utility.png) | **SOUL**<br>*Utility* | Costs **15% TP**. Hold to aim, release to tear out your SOUL and throw it up to 45m. Once it lands you are pulled straight to it, then catch it with a vertical cut for **400%** damage. |
| ![ACT](https://raw.githubusercontent.com/Luchamas/KrisModRor2/main/Build/readme-icons/Special.png) | **ACT**<br>*Special* | Stand still and choose one of **four ACTs**, drawn at random each time. Pick one with the number keys **1-4**, or jump to back out. Each one costs **TP**. |

## ACTs

| ACT | TP | Effect |
| --- | :---: | --- |
| **DARK CANDY** | 15% | Heal **15-30%** of your max health. Tastes like marshmallows. |
| **TV DINNER** | 30% | Heal **30-50%** of your max health. |
| **SCARLIXIR** | 50% | Heal **70-80%** of your max health. |
| **HOLD BREATH** | 0% | **+40%** movement speed for 15 seconds. |
| **PIROUETTE** | 20% | Spin and see what happens. Could be a blessing, could be a stumble. |
| **X-SLASH** | 20% | Two crossing cuts for **2x500%** damage. |
| **COURAGE** | 25% | **+40 armor** for you and allies within 40m for 15 seconds. |
| **BANISH** | 50% | Your SOUL rises and shines for **250%** damage, stunning enemies within 15m for 3 seconds. |
| **PETAL FEATHER** | 60% | Double jump for 15 seconds. |
| **UNLEASH** | 80% | Your SOUL blazes for **500%** damage, stunning enemies within 30m for 6 seconds. Flying enemies are slammed to the ground and rooted for 15 seconds. |
| **PROCEED** | 100% | The nearest enemy in front of you dies. *Rarely offered.* |

## Multiplayer

Everyone in the lobby needs Kris at the same version, and **the same mod list**. Online, the game identifies characters, skills and items by number rather than by name, so a mod that only some players have shifts those numbers: the others get the wrong character instead of Kris, or the wrong skills, and the log fills with errors from enemies and shrines that have nothing to do with Kris.

The easy way to match is to export your r2modman profile as a code and have everyone import it. To check, open `BepInEx/LogOutput.log` on each PC and find the line starting with `Multiplayer check:`. It must be identical for every player.

Check out my other mod: the [Flowery Character Mod](https://thunderstore.io/c/riskofrain2/p/TeamLuchamas/FloweryMod/)!

## Credits
- **Toby Fox** for making Kris and [DELTARUNE](https://deltarune.com/) as a whole (Really great game, totally recommend it)
- **sn0wtenkey** for the amazing Kris's 3D model, make sure to [check them out](https://ko-fi.com/s/66af750f15)!
- **rob**: creator of [HenryMod](https://thunderstore.io/package/rob/HenryMod/), the custom survivor
  template this mod was built on. HenryMod is now maintained by
  [TheTimesweeper](https://thunderstore.io/c/riskofrain2/p/TheTimesweeper/HenryMod/).
