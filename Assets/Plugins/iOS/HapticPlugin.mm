// HapticPlugin.mm
// Meowdoku 原生触觉桥：直接调用 iOS 系统 UIFeedbackGenerator 预设素材，零外部资源依赖。
// 与 NativeHapticDriver.cs 通过 extern "C" 函数名 MeowHaptic_Trigger / MeowHaptic_Prepare 对接。
// type 取值对应 NativeHapticDriver.HapticType：
//   0 Selection, 1 Success, 2 Error, 3 Warning, 4 Light, 5 Medium, 6 Heavy

#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator* _MeowSelGen = nil;
static UINotificationFeedbackGenerator* _MeowNotifGen = nil;
static UIImpactFeedbackGenerator* _MeowImpactLightGen = nil;
static UIImpactFeedbackGenerator* _MeowImpactMediumGen = nil;
static UIImpactFeedbackGenerator* _MeowImpactHeavyGen = nil;

static dispatch_once_t _MeowOnce;
static void MeowEnsureGenerators() {
    dispatch_once(&_MeowOnce, ^{
        _MeowSelGen = [[UISelectionFeedbackGenerator alloc] init];
        _MeowNotifGen = [[UINotificationFeedbackGenerator alloc] init];
        _MeowImpactLightGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        _MeowImpactMediumGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        _MeowImpactHeavyGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
    });
}

// 实际执行触觉反馈（必须在主线程）
static void MeowPerformHaptic(int type) {
    MeowEnsureGenerators();
    switch (type) {
        case 0: // Selection —— 轻度机械刻度
            [_MeowSelGen selectionChanged];
            break;
        case 1: // Success
            [_MeowNotifGen notificationOccurred:UINotificationFeedbackTypeSuccess];
            break;
        case 2: // Error
            [_MeowNotifGen notificationOccurred:UINotificationFeedbackTypeError];
            break;
        case 3: // Warning
            [_MeowNotifGen notificationOccurred:UINotificationFeedbackTypeWarning];
            break;
        case 4: // Light
            [_MeowImpactLightGen impactOccurred];
            break;
        case 5: // Medium
            [_MeowImpactMediumGen impactOccurred];
            break;
        case 6: // Heavy
            [_MeowImpactHeavyGen impactOccurred];
            break;
        default:
            [_MeowSelGen selectionChanged];
            break;
    }
}

// 预热：让发生器提前就绪，降低首次触发延迟（FeedbackManager 启动时调用一次）
extern "C" void MeowHaptic_Prepare() {
    if ([NSThread isMainThread]) {
        MeowEnsureGenerators();
        [_MeowSelGen prepare];
        [_MeowNotifGen prepare];
        [_MeowImpactLightGen prepare];
    } else {
        dispatch_async(dispatch_get_main_queue(), ^{
            MeowEnsureGenerators();
            [_MeowSelGen prepare];
            [_MeowNotifGen prepare];
            [_MeowImpactLightGen prepare];
        });
    }
}

extern "C" void MeowHaptic_Trigger(int type) {
    // Unity 游戏循环运行在 iOS 主线程，直接调用以最低延迟发射（满足"触控响应第一帧"）；
    // 仅在极少数非主线程回调场景下回退到 dispatch_async
    if ([NSThread isMainThread]) {
        MeowPerformHaptic(type);
    } else {
        dispatch_async(dispatch_get_main_queue(), ^{
            MeowPerformHaptic(type);
        });
    }
}
