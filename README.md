# RoboPilot-VR4IL

![RoboPilot-VR4IL-Thumbnail-Img](https://github.com/Mithilesh1103git/RoboPilot-VR4IL/blob/main/RoboPilot-VR4IL-thumbnail.png?raw=true)

**Teach Robots Through Your Hands.**

RoboPilot-VR4IL (Virtual Reality for Imitation Learning) is a virtual reality application designed to make robot teaching intuitive through immersive, human-guided demonstrations.

Built with Unity and the Meta XR platform, RoboPilot-VR4IL enables users to interact with virtual objects using tracked hand movements, demonstrate manipulation tasks, and observe a virtual robot mirror those demonstrations.

The application captures human demonstrations as structured, timestamped trajectories containing spatial poses, robot motion data, object states, and task information. These demonstrations can be inspected, replayed, and exported as structured datasets for downstream imitation-learning workflows.

### Core capabilities

- **Immersive VR Workspace:** A virtual environment for demonstrating robot manipulation tasks.
- **Hand-Tracking Interaction:** Use natural hand movements to interact with virtual objects.
- **Human-to-Robot Motion Mapping:** Translate human demonstrations into corresponding robot movements.
- **Demonstration Recording:** Capture manipulation episodes and their associated trajectory data.
- **Trajectory Replay:** Review recorded demonstrations within the VR environment.
- **Dataset Generation:** Structure and export demonstrations in a machine-readable format for imitation learning.

### Initial focus

The initial prototype focuses on a Franka Panda robotic arm and a pick-and-place manipulation task, establishing the foundation for a reusable VR-based robot demonstration and data-capture platform.

**Technology:** Unity · C# · Meta XR SDK · Hand Tracking · Robotics · Imitation Learning
