# 🎮 Killer Escape
A2-video link
https://youtu.be/-MwOu3877Ig
A2- What we have done
# 🧠 Killer NPC FSM — State & Transition Descriptions

This document outlines the finite state machine (FSM) used to control the behavior of the Killer NPC in the *Killer Escape* game. The FSM consists of the following states: **Patrol**, **Chase**, **Slash (Attack)**, and **GameOver**.

---

## 📌 States

### 🔷 Patrol

**Purpose:** Roam between waypoints to cover the map.

**Enter:**
- `NavMeshAgent.isStopped = false`
- `agent.speed = patrolSpeed`
- `Animator`:
  - `isPatrolling = true`
  - `isChasing = false`
  - `isSlashing = false`

**Update:**
- If close to current waypoint, move to the next.
- Periodically scan for player (`distance <= detectionRange`).

**Exit:**
- Player detected → Transition to **Chase**

---

### 🔶 Chase

**Purpose:** Pursue the player once detected.

**Enter:**
- `agent.speed = chaseSpeed`
- `agent.isStopped = false`
- `Animator`:
  - `isChasing = true`
  - `isPatrolling = false`

**Update:**
- Continuously `SetDestination(player.position)`
- Maintain line-of-sight and awareness.
- Check if within `attackRange`.

**Exit:**
- `distance <= attackRange` → Transition to **Slash**
- `distance > detectionRange` (after optional grace period) → Transition to **Patrol**

---

### 🟥 Slash (Attack)

**Purpose:** Play kill animation and end encounter.

**Enter:**
- Stop agent:
  - `isStopped = true`
  - `ResetPath()`
  - `updateRotation = false`
- Snap to `player.position + player.forward * slashDistance`
- Continuously face the player (coroutine)
- Disable player controls and camera scripts
- Lock camera to killer
- `Animator.isSlashing = true`

**Update:**
- Continuously face the player while animation plays

**Exit:**
- On `OnSlashAnimationComplete()` → Mark player dead and show Game Over UI

---

### 🏁 GameOver

**Purpose:** Freeze AI and present outcome.

**Enter:**
- `_isPlayerDead = true`
- Keep player inputs disabled
- Show Game Over UI / play SFX / trigger restart option

**Update / Exit:**
- Terminal state for this encounter (no transition out)

---

## 🔁 Transitions

| **From**   | **To**     | **Condition**                                        | **Side Effects**                                                                 |
|------------|------------|------------------------------------------------------|----------------------------------------------------------------------------------|
| Patrol     | Chase      | `distance(player) <= detectionRange`                | Set `isChasing = true`, `isPatrolling = false`, play chase SFX/UI               |
| Chase      | Slash      | `distance(player) <= attackRange`                   | Stop agent, snap to player, disable inputs, set `isSlashing = true`             |
| Chase      | Patrol     | `distance(player) > detectionRange`                 | Reset speed to `patrolSpeed`, resume waypoints, set `isPatrolling = true`       |
| Slash      | GameOver   | On `OnSlashAnimationComplete()` animation event     | `_isPlayerDead = true`, freeze AI, show Game Over                               |

---

## ✅ Notes

- The FSM logic is designed to work with Unity’s `NavMeshAgent` and `Animator` components.
- Use coroutines to handle smooth transitions during Slash and camera lock sequences.
- Optional transitions (like grace periods) can be fine-tuned for balance and fairness.

---

📁 For more implementation details, refer to the `KillerAI.cs` script in the project’s `Scripts/AI/` directory.

## 📌 Overview
*Killer Escape* is a **3D first-person survival horror and puzzle game** where one or two players explore a seemingly abandoned building. After finding a way into the main area, they are captured by the being within and dragged into the deepest depths of the structure. Players must use their **wits and stealth abilities** to escape without being caught again.

---

## 🕹️ Core Gameplay
- Sneak around the structure solving puzzles.  
- Avoid the patrolling beast that hunts the players.  
- Hide while completing tasks to survive.  

---

## 🎯 Game Type
**First-Person Survival Horror & Puzzle Adventure**

---

## 👥 Player Setup
- **Single Player:** Explore as the sole survivor.  
- **Co-op (Optional):** Work together to solve puzzles and escape.  

---

## 🤖 AI Design

### Killer FSM
- **Patrol** – Roams waypoints, listens for sounds, looks for silhouettes.  
- **Place Trap** – Semi-random trap placement to slow/alert on trigger.  
- **Investigate** – Moves to last sound/sighting, performs scan.  
- **Chase** – Sprints at target in line-of-sight, updates dynamically.  
- **Search** – Lost target? Fans out, checks hiding spots.  
- **Stun/Blocked** – Temporarily disabled by traps/doors; resumes after.  
- **Capture** – Jumpscare + failure state when player is caught.  

### Puzzle FSM
- **Inactive** – Idle, waiting for player.  
- **Locked** – Player interacts without required item. Emits noise → alerts Killer.  
- **Solving** – Player has correct key item/passkey. Begins interaction.  
- **Solved** – Puzzle completed; unlocks door/trap/room. No longer interactable.  

### Item FSM
- **Available** – Item visible in the world, can be picked up.  
- **Picked Up** – Added to inventory, removed from environment.  
- **Used** – Consumed for effect (unlock, heal, etc.).  
- **Gone** – Removed permanently.  

---

## 🎬 Scripted Events
- Killer places traps in weighted random rooms (based on player sightings).  
- First section ends with a scripted capture → gameplay shifts to escape.  
- Patrol behavior includes random “look around” checks for realism.  
- When spotted, Killer pursues until LOS is lost → then checks hiding spots.  

---

## 🌍 Environment
- Grimy **abandoned house interior** with sprawling rooms.  
- Multiple locked doors and puzzles.  
- **NavMesh-baked** layout for AI pathing.  
- Interactive props: doors, drawers, closets, locks, keys, buttons, puzzles.  

---

## 🧪 Physics Scope
- **Predictable & fair physics** for stealth, puzzles, and navigation.  
- Lightweight simulation for stable co-op frame times.  
- Quiet interactions by default; loud physics (slams, traps) are intentional.  

---

## 🧠 FSM Scope
- FSMs for **Killer AI, Player, Puzzles, Doors/Locks, and Items**.  
- Event-driven transitions using **Unity Events / C# events**.  
- Timers for chase/search cooldowns, puzzle delays, locker checks.  
- Blackboard-style memory: `playerLastSeenPos`, `playerLastHeardPos`, `suspicion`, `hasKeyItem`.  
- Noise system: footsteps, trap triggers, and doors generate sound pings.  

---

## 🧩 Systems & Mechanics
- **Suspicion meter** – rises when heard/spotted, falls when hidden.  
- **Object tagging** – `Key`, `Lock`, `Puzzle`, `Trap`.  
- **Perspective** – First-person camera attached to player object.  
- **Audio cues** –  
  - *Boom* when spotted.  
  - *Chase music* when pursued.  
  - *Dark ambience* when undetected.  
  - *Heartbeat* when hiding.  
  - Footsteps of both player and killer.  

---

## 🎮 Controls
```plaintext
W A S D  - Move
Mouse    - Look
Mouse1   - Use Item
E        - Interact (pick up, drop, close door, disarm)
Q        - Drop held item
Ctrl     - Crouch
Esc      - Pause
