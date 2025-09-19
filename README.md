# CISC486-Game-Dev-Killer-Escape
🎮 Killer Escape
📌 Overview
Killer Escape is a 3D first-person survival horror and puzzle game where one or two players go to explore a seemingly abandoned building. Although after they find a way into the main area they are caught by the being within the building and dragged into the deepest depths of the structure. They must now use their wits and stealth abilities to escape without being caught again.
🕹️ Core Gameplay
Players must sneak around the structure solving various puzzles while avoiding the beast that resides there patrolling for them. They must hide while completing tasks to hopefully leave with their lives.
🎯 Game Type
First-Person Survival Horror and Puzzle Adventure
👥 Player Setup
Single player as the sole explorer
Optional co-op where the players must solve the puzzles together
🤖 AI Design
Killer FSM
Idle (optional startup)
 Brief pause before entering Patrol (spawn, cutscene).
Patrol
Roams between waypoints, does ambient checks. Listens for sounds; looks for player silhouettes.
Place Trap
Chosen using a semi-random system, the killer will place a visible trap that will alert the killer if triggered and slow down the player.
Investigate
 Moves to last sound or suspicious sighting location; performs short scan.
Chase
 Sprints toward confirmed players with line-of-sight; updates target on each tick.
Search
 Lost target? Fan-out search around last known position; check closets/lockers if nearby.
Stun/Blocked (optional)
 Temporarily disabled (player trap or door slam). Resumes to Patrol/Investigate afterward.
Capture
 Triggers jumpscare & failure pipeline when close enough and unobstructed.
Puzzle FSM
Inactive
Puzzle is idle and waiting for player interaction.
Locked
Player interacts without the required key item/passkey.
Puzzle remains unsolved, emits a noise event that alerts the Killer.
After noise feedback, puzzle returns to Inactive.
Solving
Player has the required key item/passkey.
Puzzle interaction begins (UI/mini-game/animation).
Killer may still patrol or be drawn by puzzle noise (optional).
Solved
Puzzle is completed successfully.


Unlocks the corresponding door/trap/room.
No longer interactable (or becomes “checked” state).
Puzzle FSM
Available
Item exists in the world, visible and interactable.
Can be picked up by the player.
Picked Up
Item is removed from the environment.
Added to the player’s inventory.
Used
Item is consumed (e.g., key unlocks a door, medkit restores health).
Removed from inventory.
Gone
Item no longer exists (cannot be picked up or reused).
🎬 Scripted Events
The enemy that pursues the player will set a trap in a room randomly selected with this choice being weighted based on rooms the players were spotted.
When the first section is completed, a scripted event where the player/players get caught and the objective is changed from exploration to escape.
The enemy when patrolling will randomly choose to look around using a system that will have the looking around feel random but not constant (multiple pass checks needed or minimum time cooldown)
When the enemy spots you they will pursue until you have escaped line of sight for a period of time/hiding, where they will check a nearby hiding spot then return to patrolling.
🌍 Environment
Interior grimy house scene with sprawling rooms full of locked doors and puzzles
 NavMesh baked for AI pathing
 Interactive props with colliders such as doors, drawers, closets, locks, keys, buttons, and other puzzles
🧪 Physics Scope
Deliver predictable, fair, low-noise physics that supports stealth, puzzles, and AI navigation.
Keep simulation lightweight for stable frame times and smooth co-op.
Prioritize quiet interactions by default; “loud” physics (drops, slams, traps) should be intentional and gameplay-relevant.
🧠 FSM Scope
Finite state machines implemented for Killer AI, Player, Puzzles, Doors/Locks, and Items.
Event-driven transitions using Unity Events / C# events. (e.g., SeenPlayer, HeardNoise, PuzzleComplete)
Timers for investigate/search/chase windows, locker-check cooldowns, and puzzle solve/deny delays.
Blackboard-style memory for playerLastSeenPos, playerLastHeardPos, suspicion (0–100), searchEnvelope, hasKeyItem(passkeyId).
Noise system integration: footsteps, trap tiles, door slams emit pings that drive AI transitions.
🧩 Systems and Mechanics
 Suspicion meter increases when spotted or heard and deplenishes when player is unseen and unheard
 Object tagging Key, Lock,  Puzzle, Trap
 Camera attached to player object to provide a first person perspective
 Audio cues a sharp boom when player spotted, chase music when pursued, dark ambience when not spotted, Heartbeat when hiding, and footsteps of both player and killer.
🎮 Controls
W A S D move
Mouse look
Mouse1 Use Item
E interact pick up, drop, close door, disarm
Q drop Held item
Esc pause
Ctrl Crouch
(Controls are the same additional players)
📂 Project Setup aligned to course topics
Unity (6.2) C# scripts for PlayerController, GameController, ItemManager, EnemyController
NavMesh for AI pathing
Animator controllers for player characters and monster based on their current action
Physics materials and layers configured in Project Settings
GitHub repository with regular commits and meaningful messages
Readme and in game debug UI showing FPS, state names, and safety meter for assessment
