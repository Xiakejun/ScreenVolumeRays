# ScreenVolumeRays

屏幕后处理体积光

VibeCoding改编自 https://www.kodeco.com/22027819-volumetric-light-scattering-as-a-custom-renderer-feature-in-urp

由于作者大大用的是比较早版本的URP，且Unity更新管线ApI很快，所以早期仓库内容并不能直接使用在Unity6中，且由于Unity在逐渐推崇rendergraha，早期管线API在逐渐淘汰

（6000.3.15已经隐藏了老API兼容，默认使用rendergrapha），所以在此对其进行改编更新。

所以重构了api为render grapha，使其在新版urp也能正常适配，然后是将其内嵌置URP原生的后处理系统即Volume组件，可以在Volume组件中进行调整参数

注意，依旧需要在URP资产中把VolumetricLightScattering添加至管线

其次是一些修改改进，比如对于光源与摄像机方向完全垂直时（即光源在摄像机的正侧方），导致的效果突变异常，加入并暴露了衰减参数，从而达到渐变效果防止突兀

修改了颜色的采样算法，因为原算法中，背光时由于采样不到太阳光源导致背光时的"光照强度"远小于面光时，令人突兀，故使用统一参数干预算法效果。(此方法是否真的合适有待商榷)

![效果图1](https://github.com/user-attachments/assets/83c43e4a-d576-49be-8406-6ef3ecab7679)

![效果图2](https://github.com/user-attachments/assets/4e621311-1655-4766-8099-fff44ac93519)
<img width="2216" height="1296" alt="_ RGKYKK%G4}%%}$IYR~WR1" src="https://github.com/user-attachments/assets/ba1ef8a0-e718-42d7-bdad-b8e2e8f8e537" />
