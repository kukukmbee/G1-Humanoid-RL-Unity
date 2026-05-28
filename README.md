# 🤖 Unitree G1 Humanoid Walking via PPO Reinforcement Learning in Unity

An advanced Deep Reinforcement Learning (DRL) project training the **Unitree G1 Humanoid Robot (29 Degrees of Freedom)** to walk stably, navigate turns, and perform delivery missions in a 3D corridor environment using **Unity ML-Agents** and **PyTorch (PPO)**.

---

## 🌟 Key Features

* 🦵 **Active Suspension Gait (Pre-Flexed Knees):** Configured default 25° knee flexion at initialization to act as natural mechanical dampers, overcoming the classic straight-legged "stilt-walker" reinforcement learning local minimum.
* 💆‍♂️ **Biomechanical Head Alignment Reward:** A custom-engineered DRL reward that keeps the robot's head centered laterally (<7cm offset) and aligned slightly forward of the pelvis (1-8cm), enforcing a realistic, slightly forward-leaning athletic human posture.
* 🔄 **Offset-Based Joint Control:** Continuous actions from the neural network (-1 to 1) map to joint targets via deviations (±65°) from a stable standing posture rather than absolute angles. This ensures rapid convergence and stable training starts.
* 🗺️ **Procedural Corridor Map Generator:** The entire 3D level—featuring thick concrete floors (preventing physics clipping), corridor walls forming an S-curve, target boxes, and waypoints—is generated programmatically in **1 frame** at runtime.
* 📦 **C# High-Level Waypoints Mission Manager:** Decouples complex locomotion from pathfinding. A high-level C# script guides the robot through sequential turns to the box, "picks up" the box (parents it to the chest/hands, disabling box physics to prevent jitter), and guides it back home.
* 🔄 **Automated Mission-RL Sync Reset:** If the robot falls and the DRL episode resets, the mission controller automatically catches the event, resets the box to the starting position, and restarts the path route.

---

## 🛠️ Tech Stack

* **Physics Engine:** Unity PhysX (using `ArticulationBody` Solver with 30 solver iterations and 12 velocity iterations for high joint precision).
* **Machine Learning Library:** Unity ML-Agents Toolkit (v2.3+).
* **Deep Learning Framework:** PyTorch.
* **Inference Engine:** Unity Barracuda.
* **Algorithm:** Proximal Policy Optimization (PPO).

---

## 🧠 DRL Hyperparameters (`g1_walking_config.yaml`)

Our customized state-of-the-art PPO training configuration ensures highly stable linear reward growth:

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
      hidden_units: 256
      num_layers: 3
      vis_encode_type: simple
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    keep_checkpoints: 5
    max_steps: 10000000
    time_horizon: 1000
    summary_freq: 20000
```

---

## 🚀 How to Run the Project

### Option A: Play Pre-Trained Model (Inference Mode)
You don't need Python or Anaconda to watch the robot walk! The pre-trained neural network is embedded directly in Unity.

1. Open the project in the **Unity Editor (2022.3+)**.
2. Locate the pre-trained neural network: `C:\Users\teplicaa\results\G1_Run_7\G1Walking\G1Walking-4999976.onnx` (or drag the latest `.onnx` from the repository into your Assets folder).
3. Select the robot GameObject `g1_28dof` in the hierarchy.
4. In the **Behavior Parameters** component:
   * Drag your `.onnx` file into the **Model** field.
   * Change **Behavior Type** to **`Inference Only`**.
5. Press the **Play** button in Unity! Watch the robot navigate the procedural S-corridor, pick up the orange box, and deliver it to the base.

---

### Option B: Train from Scratch (Training Mode)

If you want to train the model yourself using Python:

1. Open the **Anaconda Prompt** and activate your ML-Agents environment:
   ```bash
   conda activate mlagents
   ```
2. Navigate to your project folder:
   ```bash
   cd F:\unity\ml
   ```
3. Start training with our custom config file:
   ```bash
   mlagents-learn g1_walking_config.yaml --run-id=G1_Run_8 --force
   ```
4. Press the **Play** button in the Unity Editor to start the physics simulation.
5. *(Optional)* Monitor training graphs in real-time in a second Anaconda terminal:
   ```bash
   tensorboard --logdir C:\Users\teplicaa\results
   ```
   Open `http://localhost:6006/` in your browser.

---

## 📂 Project Structure

```text
Assets/
 └── Scripts/
      ├── G1Agent.cs              # Core DRL Agent: Observations, posture rewards, joint control offsets
      ├── G1MissionController.cs  # High-level C# waypoint pathfinding, box pickup, and mission sync reset
      └── G1MapGenerator.cs       # Procedural 3D S-corridor map level generator
```

---

## 🏆 Training Progress Results

* **Step 20,000:** `Mean Reward: 0.35` (Standing stabilization)
* **Step 200,000:** `Mean Reward: 3.42` (First confident steps, learning leg swing coordination)
* **Step 1,000,000:** `Mean Reward: 15.12` (Stable walking gait, learning turn balance)
* **Step 3,000,000:** `Mean Reward: 122.34` (Full S-corridor waypoint traversal)
* **Step 5,000,000:** `Mean Reward: 141.99` (Fully solved locomotion and delivery mission success rate!)
