# ScreenVolumeRays
屏幕后处理体积光
VibeCoding改编自https://www.kodeco.com/22027819-volumetric-light-scattering-as-a-custom-renderer-feature-in-urp
由于作者大大用的是比较早版本的URP，且Unity更新管线ApI很快，所以早期仓库内容并不能直接使用在Unity6中，且由于Unity在逐渐推崇rendergraha，早期管线API将可能在以后不支持
（6000.3.15已经隐藏了老API兼容，默认使用rendergrapha），所以在此对其进行改编更新。
首先是重构了api为render grapha，使其在新版urp也能正常适配，然后是将其内嵌置URP原生的后处理系统即Volume组件，可以在Volume组件中进行调整参数
注意，依旧需要在URP资产中把VolumetricLightScattering添加至管线
其次是一些修改改进，比如对于光源与摄像机方向完全垂直时（即光源在摄像机的正侧方），导致的效果突变异常，
加入并暴露了衰减参数，从而达到光源中心向屏幕边缘的衰减效果
修改了颜色的采样算法，因为原算法中，背光时由于采样不到太阳光源导致背光时的“光照强度”远小于面光时，令人突兀，故使用统一参数干预进行颜色算法修缮。此项修改是否合适以及需要改动待定。<img width="2216" height="1296" alt="_ RGKYKK%G4}%%}$IYR~WR1" src="https://github.com/user-attachments/assets/4d46bb80-1617-4ca1-a4c1-b5767410731b" />

<img width="2217" height="1290" alt="1TM@HR84}~83 1R`(I0N55E" src="https://github.com/user-attachments/assets/83c43e4a-d576-49be-8406-6ef3ecab7679" />
<img width="2559" height="1438" alt="DUE`GG0`WY)Q)O5)9O{90KL" src="https://github.com/user-attachments/assets/4e621311-1655-4766-8099-fff44ac93519" />
