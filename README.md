# Three.js 与 Unity 频谱海面实验

这是一个可直接在浏览器或 Unity 中运行的实时 3D 海面实验。它以非整周期、多尺度的波谱生成海面：低频涌浪会扭曲后续波段的采样域，不同方向、波长、速度与波群包络相互调制，避免整片水面按同一节拍重复起伏。场景包含礁石与严格跟随水面函数的航标浮标，便于观察尺度、遮挡、法线和运动连续性。

## Three.js 画质预览

![Three.js Extreme 海面画质：流畅、高、极致](assets/threejs-ocean-quality-modes.gif)

上图依次展示 Three.js Extreme 的流畅、高、极致三档画质（约 10 秒循环）。

## Unity 画质预览

![Unity Extreme 海面画质：流畅、高、极致](assets/unity-ocean-quality-modes.gif)

上图依次展示 Unity Extreme 的流畅、高、极致三档画质（约 10 秒循环）。

## 快速开始

| 运行环境 | 入口 | 操作 |
| --- | --- | --- |
| Three.js 标准版 | `threejs/index.html` | 在项目根目录运行 `python3 -m http.server 8000`，再打开 `http://localhost:8000/threejs/`；拖拽镜头可下潜到水下。 |
| Three.js Extreme | `threejs/extreme/index.html` | 使用同一服务打开 `http://localhost:8000/threejs/extreme/`；可向下拖拽下潜，按 `1 / 2 / 3` 或使用左下角按钮切换画质。 |
| Unity Built-in | `unity` | 在 Unity Hub 中打开 `unity`，选择 `Water Comparison > Create cinematic ocean scene`，打开 `Assets/Scenes/WaterComparison.unity` 后 Play。 |
| Unity Extreme | `unity/Assets/Scenes/WaterComparisonExtreme.unity` | 选择 `Water Comparison > Create EXTREME ocean scene` 后 Play；按 `1 / 2 / 3` 切换流畅、高、极致画质。 |

不要直接双击 Three.js 的 HTML 文件；它们使用 ES Module，应通过本地 HTTP 服务访问。

> 根目录的 HTML 指南页和个人学习 Markdown 文档已通过 `.gitignore` 排除，不会发布到仓库。

## 当前实现

- **非周期位移：** Three.js 使用 8 个不可公度波段、波群包络和低频域扭曲；Unity 使用 6 个不可公度波段，并由前两层长涌浪扭曲后续波段。两端都在 GPU 顶点阶段执行水平与垂直 Gerstner 位移。
- **法线与泡沫：** 大尺度法线由位移函数解析导数重建，微表面再叠加多方向毛细波。泡沫来自水平位移 Jacobian 的压缩程度，并用风向拉伸噪声打散，而不是按高度画固定白边。
- **水体材质：** 采用水的 IOR 1.333（法向入射 F0 约 2%）、Schlick Fresnel、GGX 太阳高光和 Beer–Lambert 吸收/散射。Unity 项目使用 Linear 色彩空间，避免 Gamma 下的塑料感和过曝。
- **运动同步：** 航标的 CPU 高度、水平漂移与法线计算使用和各自 Shader 相同的频谱参数，不会再穿过水面或与浪峰错相。

## Three.js Extreme 版

标准版保持不变；Extreme 版位于独立目录，额外加入：

- 12 个非共周期波段、真正的水平位移 Jacobian 碎浪判据，以及更大的 170 × 170 水面。
- 镜像相机、斜裁剪平面和独立 HDR RenderTarget 生成的场景级平面反射；礁石与浮标能够真实出现在水面倒影中。
- 独立折射场景、Beer–Lambert 水体衰减、GGX 太阳闪光、程序化海床焦散和礁石接触泡沫。
- 三档网格、DPR、反射/折射分辨率与更新频率，并在标签页隐藏时彻底暂停动画。
- 每档再拆为自动运行层级；极致档保持 2× DPR、2048 上限离屏纹理和完整波谱，但会将反射与折射交错到相邻帧，避免同一帧连续执行两次完整离屏场景渲染。HUD 的 `LOAD` 会显示当前是否处于自动保护层级。
- Seeded ridged multifractal 程序化礁石：真实改变剪影的切削断面、层理、裂隙、碎石，以及更暗更光滑的湿润水线。

## Unity Extreme 版

原 Unity 场景保持不变；Extreme 场景和资源位于 `Assets/WaterComparisonExtreme`，额外加入：

- 160 米、12 波段非共周期 Gerstner 海面，三档网格为 160 / 240 / 320；浮标使用同一套 CPU 波谱同步升沉、横移和倾斜。
- 主相机专用的斜裁剪平面反射、屏幕折射、深度前景保护、IOR 1.333 Fresnel、GGX 高光和 Beer–Lambert 吸收散射。
- 256 / 512 的 30 Hz 历史碎浪模拟，泡沫会平流、衰减并持续注入；礁石接触泡沫使用深度和双尺度噪声形成不规则薄带。
- 7 块 seeded 几何礁石，包含真实切面、层理、裂谷和随水线变化的干湿粗糙度；海床带动态焦散。
- 热量保护：高/极致档在高刷屏上按刷新率选择同步间隔并限制到不高于 60 FPS；失焦时暂停反射和泡沫并降到 15 FPS。流畅档不再执行未使用的平面反射。
- 显存保护：反射最长边限制为 1280，opaque scene 最长边限制为 1920；RenderTexture 创建失败时不会继续安装无效的渲染命令。

## 性能处理

- Three.js 水面从 260 × 260 降到 176 × 176 分段，顶点与三角形数量约减少 54%；移除了多遍 Bloom/Composer，改为单次主渲染，并根据帧耗在 0.8–1.6 DPR 之间自适应。
- Unity 水面默认从 250 × 250 降到 200 × 200 分段，水面顶点约减少 36%；波数、波数向量与角频率预计算，顶点阶段避免重复开方和除法；关闭未使用的深度纹理与动态批处理，MSAA 调整为 2×。
- Unity Extreme 默认使用 240 × 240 网格、半分辨率反射、0.85 倍且最长边不超过 1920 的折射源；仅极致档升到 320 × 320、0.65 倍反射和 512 泡沫历史。编辑器 Scene/Preview 相机不会再覆写 Game 相机反射状态。
- Unity 6.5 Metal 编辑器的当前 Game 视图测试约为 104k 三角形、60.9k 顶点、19 SetPass、42 Draw Calls；实际帧率仍应以目标分辨率的独立构建和 Unity Profiler 为准。

如果要换成实际 3D 水面模型，确保水面区域具有均匀且足够的顶点密度；然后替换 Unity 物体的 MeshFilter 网格或 Three.js 的 `PlaneGeometry`。若目标是大世界海洋，下一步应改用相机中心环形网格、FFT 波谱和屏幕空间/平面反射，而不是继续增加平面细分。
