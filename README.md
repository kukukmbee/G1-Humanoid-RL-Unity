# Unitree G1 Humanoid Locomotion via PPO Reinforcement Learning

This repository contains a project for training a 29 Degrees of Freedom (DoF) Unitree G1 humanoid robot to walk upright using Unity ML-Agents and PyTorch-based Proximal Policy Optimization (PPO).

## Project Status
The project is currently in the active training phase. The agent is learning to maintain balance, coordinate joint movements, and stabilize its torso to achieve stable upright walking.

## Implementation Details

### Starting Pose Flexion
The robot is initialized with a default 25-degree knee flexion target. This pre-flexed starting pose prevents straight-legged singularities and stabilizes the physical contact solver.

### Torso and Head Alignment Reward
A custom reward function encourages upright locomotion by analyzing the position of the head relative to the pelvis:
* **Lateral Alignment:** Penalizes lateral head drift greater than 7 centimeters to minimize side-to-side waddling.
* **Sagittal Alignment:** Rewards keeping the head within a 1 to 8 centimeter range in front of the pelvis (representing a natural forward walking lean) while penalizing excessive slouching or backward leaning.

### Offset-Based Control Space
The continuous action space outputs (-1 to 1) are mapped to joint target offsets (±65°) relative to the default standing pose rather than absolute joint values. This limits high-frequency joint oscillations and improves training stability.

## Hyperparameters

Locomotion training is configured using Proximal Policy Optimization (PPO) with the following parameters:

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
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    keep_checkpoints: 5
    max_steps: 10000000
    time_horizon: 1000
    summary_freq: 20000
```

## Running the Project

### Inference Mode
To run a trained locomotion model in the Unity Editor:

1. Open the project in the Unity Editor.
2. Import a trained `.onnx` model into the Assets directory.
3. Select the `g1_28dof` GameObject in the hierarchy.
4. In the **Behavior Parameters** component:
   * Assign the `.onnx` model to the **Model** field.
   * Set **Behavior Type** to **`Inference Only`**.
5. Press **Play** in the Unity Editor.

### Training Mode
To train the model from scratch:

1. Open a terminal and activate the ML-Agents conda environment:
   ```bash
   conda activate mlagents
   ```
2. Navigate to the project root directory.
3. Execute the training command:
   ```bash
   mlagents-learn g1_walking_config.yaml --run-id=G1_Walking_Run --force
   ```
4. Press **Play** in the Unity Editor to start the physics simulation.
5. Monitor the training progress using TensorBoard:
   ```bash
   tensorboard --logdir=results
   ```
