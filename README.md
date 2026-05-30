# Unitree G1 Humanoid Locomotion via PPO Reinforcement Learning

This repository contains a project for training a 29 Degrees of Freedom (DoF) Unitree G1 humanoid robot to walk upright using Unity ML-Agents and PyTorch-based Proximal Policy Optimization (PPO).

---

## Project Status & Current Configuration

* **Active Run:** `G1_Run_10`
* **Observation Space Size:** **74** (dynamic observation vector)
* **Action Space:** 29 continuous actions (mapped to PD controller joint offsets)
* **Training Target:** 20,000,000 steps

---

## G1Agent Script Evolution Map

Here is a concise breakdown of how the locomotion agent and reward architecture evolved across training runs:

### 1. Initial Phase (Runs 1–8)
* **Observation Space:** `66` (Pelvis world position/rotation + 29 Joint angles/velocities).
* **Target Vector:** World-space delta calculations.
* **Spawn Logic:** Fixed spawn orientation (always facing target).
* **Speed Reward:** Based on coordinate delta between steps (noisy gradient).

### 2. Major Upgrade Phase (Run 9)
* **Observation Space:** `72` (Added pelvis linear and angular velocities).
* **Locomotion:** Target vector converted to local space (`InverseTransformDirection`) for rotation-invariant learning.
* **Speed Reward:** Switched to instantaneous physical pelvis velocity projection (`linearVelocity`) towards target.
* **Rotations:** Widen spawn yaw range to `±180°` to force learning of active turning.
* **Posture:** Added global sagittal projection lean reward and severe `0.1x` speed penalty for backwards tilt (preventing "Matrix dodge" cheat).

### 3. Ultimate Gait Phase (Run 10 - Current)
* **Observation Space:** `74` (Integrated foot contact state sensors).
* **Foot Sensors:** Added biinar `FootSensor` states (grounded/airborne) to observations, eliminating shuffling/sliding and enabling natural human walk cycle.
* **Reward Balance:**
  * Upright balance coefficient boosted from `0.002` to **`0.015`** (making stability priority #1).
  * Posture lean bonus reduced from `0.02` to **`0.004`** (stopping lean-exploit).
  * Speed multiplier adjusted to `0.2` and restricted to strictly forward motion.
  * Jerkiness/energy penalty normalized by actions length and boosted 10x to **`0.025`** for smooth joint transitions.
* **Bug Fixes:**
  * Fixed finishing target reward from `SetReward(10f)` (which wiped out episode history) to `AddReward(10f)`.
  * Resolved joint reset "mixed units" bug (all joints now reset strictly in radians based on `defaultTarget`).
  * Cleared C# lists on `Initialize()` to prevent Editor heap duplicates.

---

## Implementation Details

### offset-Based Control Space
Instead of outputting absolute joint angles, the network outputs normal continuous values (-1 to 1) which map to joint target offsets (±65°) relative to a pre-defined spring-like default pose (bent elbows, splayed knees). This greatly improves stability and speeds up physics convergence.

### Foot Ground Sensors
Two binary `FootSensor` components on G1's soles track floor collisions. By observing the stance vs swing phase, the agent avoids floating, sliding, or sliding-gait patterns, forcing PPO to discover a true, rhythmic walking stride.

---

## Hyperparameters

Locomotion training is configured using the following YAML parameters (`g1_walking_config.yaml`):

```yaml
behaviors:
  G1Walking:
    trainer_type: ppo
    hyperparameters:
      batch_size: 2048
      buffer_size: 20480
      learning_rate: 3.0e-4
      beta: 5.0e-3
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 5
      learning_rate_schedule: linear
    network_settings:
      normalize: true
      hidden_units: 512
      num_layers: 3
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    keep_checkpoints: 5
    max_steps: 20000000
    time_horizon: 1000
    summary_freq: 20000
```

---

## Running the Project

### Inference Mode (Play Mode)
1. Open the project in the Unity Editor.
2. Select the G1 robot GameObject prefab.
3. In **Behavior Parameters**:
   * Set **Space Size** to **`74`**.
   * Assign the trained `.onnx` model (e.g. `G1Walking.onnx`) to the **Model** field.
   * Set **Behavior Type** to **`Inference Only`**.
4. Attach `FootSensor` components to left/right foot objects and link them in G1Agent slots.
5. Press **Play**.

### Training Mode
1. Open a terminal and activate the ML-Agents conda environment:
   ```bash
   conda activate mlagents
   ```
2. Navigate to the project root directory:
   ```cmd
   cd /d F:\unity\ml
   ```
3. Execute the training command:
   ```bash
   mlagents-learn g1_walking_config.yaml --run-id=G1_Run_10
   ```
4. Press **Play** in the Unity Editor to start physics simulation.
