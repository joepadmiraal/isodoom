namespace IsoDoom.Render;

public static partial class HoldPoses
{
    // T6.14a: the poses' data, ours (SPEC §12 "T6.14 Player weapons"); every mask and the muzzle
    // are derived from the WAD's lump at load (HoldPoses.Derive). One line a lump, tokens:
    //   hash=H              ContentHash of the image the line was made for (else no pose)
    //   front|behind        the layer
    //   seed=X,Y[;X,Y]      texels on the rifle: an 8-connected flood over its colours (IsRifleColour)
    //   box=X0,Y0,X1,Y1     the flood stays inside (inclusive)
    //   add=RECT[;RECT]     texels added to the rifle whatever their colour (lit parts), crossed by the flood
    //   cut=RECT[;RECT]     texels taken out of the rifle (the belt, the neck)
    //   flash=X,Y fbox=RECT frame F's muzzle flash: a flood over the flash colours (IsFlashColour) in fbox
    //   over=RECT[;RECT]    the hands: the body texels there drawn over a front weapon
    //   grip=X,Y            where the rear hand holds the weapon
    //   tip=X,Y             the muzzle is the axis end nearer this (else the end farther from the grip)
    //   muzzle=X,Y          the muzzle itself, where the rifle points at the camera (no axis to follow)
    //   fs=F                the foreshortening itself (else the rifle's length over the frame's reference)
    //   hands=X,Y[;X,Y]     T6.14c (front poses): where each visible hand holds the rifle, the rear hand first
    //                       (the weapon's grip), then the forward hand (the weapon's axis passes through it)
    // `ref F=LENGTH@R` is frame F's reference length, its rotation R lump's rifle length (HoldPoseTests
    // checks it): the side view (3) in the aiming frames E and F, the longest view elsewhere.
    private const string Data = """
        ref A=40.90@1 B=38.99@1 C=40.55@1 D=40.24@1 E=23.44@3 F=20.14@3 G=39.63@1
        PLAYA1 hash=825595a64ac8a0da front hands=4.5,23;30,19.5 seed=30,15 box=0,13,40,29 add=35,11,40,12 grip=4.5,23 over=0,16,7,25;26,16,33,22
        PLAYA2A8 hash=7b2650b013f5228a front hands=8.5,26.5;29.5,17 seed=28,13 box=0,8,34,29 cut=0,0,19,10;23,20,34,29 grip=8.5,26.5 over=27,15,33,19;7,25,10,28
        PLAYA3A7 hash=5b361dce072c2734 front hands=8,20 seed=5,13 box=0,11,12,29 grip=8,20 tip=5,11 over=4,17,11,21
        PLAYA4A6 hash=0d12b4d8ce4069c1 behind seed=0,12;12,24 box=0,10,16,27 grip=6,19 tip=0,10
        PLAYA5 hash=6dbf830411659bd4 behind seed=0,13 box=0,10,8,19 grip=7.5,17 tip=0,11
        PLAYB1 hash=ad2cd305b1c95336 front hands=3.5,23;30,20 seed=30,13 box=0,10,36,30 cut=0,0,22,12 grip=3.5,23 over=0,16,8,26;25,16,33,23
        PLAYB2B8 hash=585c64c98b33071f front hands=8,28;29,19 seed=29,9 box=0,8,34,30 cut=0,0,25,10;0,11,21,11;24,18,34,30 grip=8,28 over=5,26,10,29;25,15,33,22
        PLAYB3B7 hash=730704d985e279ae front hands=8,24;7,19 seed=0,20 box=0,9,9,29 grip=7,20 tip=5,9 over=2,16,9,25
        PLAYB4B6 hash=9cfb2523e7eb8920 behind seed=0,10;12,24 box=0,9,19,27 grip=5,18 tip=0,9
        PLAYB5 hash=6486a5550b94dc23 behind seed=0,10 box=0,9,8,14 grip=7,15 tip=0,10
        PLAYC1 hash=607e7e5068c8793c front hands=3,21;29,21 seed=33,13 box=0,10,37,30 cut=0,0,15,12 grip=3,21 over=0,16,7,22;25,17,32,23
        PLAYC2C8 hash=feae988d846b26bf front hands=6,28 seed=24,13 box=0,12,36,30 cut=0,0,21,12;22,21,36,30 grip=6,28 over=4,26,9,29;26,15,34,25
        PLAYC3C7 hash=57c86052d5f8a9c4 front hands=5,26;5,20 seed=2,13 box=0,11,7,29 grip=5,20 tip=2,11 over=1,17,10,28
        PLAYC4C6 hash=51de8e54e657ec7e behind seed=1,12;14,23 box=0,11,22,27 grip=7,18 tip=0,11
        PLAYC5 hash=20bf4f28e77e66b2 behind seed=0,13 box=0,12,4,15 grip=5,16 tip=0,12
        PLAYD1 hash=539f1768666282bc front hands=4,22;29,19.5 seed=36,15 box=0,12,39,30 cut=0,0,20,11 grip=4,22 over=0,15,8,23;25,16,33,23
        PLAYD2D8 hash=a0d076abae79982a front hands=8,28;27,20 seed=29,14 box=0,13,34,30 cut=20,22,34,30 grip=8,28 over=6,26,10,29;20,15,32,23
        PLAYD3D7 hash=b1fce01aa37e445e front hands=6,28;7,21 seed=0,20 box=0,13,9,30 grip=7,21 tip=4,14 over=1,18,12,29
        PLAYD4D6 hash=41609c41c6b196ca behind seed=0,13;16,24 box=0,12,22,28 grip=8,19 tip=0,13
        PLAYD5 hash=b39a2668ed45818c behind seed=0,14 box=0,12,6,17 grip=7,16 tip=0,13
        PLAYE1 hash=5ea6ed487b8b62bb front hands=8,24;2,25 seed=3,22 box=0,19,13,27 cut=7,27,13,27 grip=8,24 muzzle=3.5,22.5 over=0,21,22,27
        PLAYE2E8 hash=f931e9a0e37516f9 front hands=19,25 seed=2,21;18,29;23,26 box=0,17,27,29 grip=19,25 over=14,19,27,28
        PLAYE3E7 hash=6ea01c7971c2822f front hands=20,24.5 seed=2,20;21,28 box=0,16,24,28 grip=20,24.5 over=17,16,31,28
        PLAYE4E6 hash=f17d643ce2177493 behind seed=0,19 box=0,18,6,21 grip=8,20 tip=0,19.5
        PLAYE5 hash=1c01d42f34a6888b behind seed=19,22 box=16,19,23,26 grip=19.5,20 tip=19,18
        PLAYF1 hash=209a04a4ddd4a057 front hands=8,24;2,25.5 seed=3,25 box=0,22,13,27 cut=7,27,13,27 flash=3,21 fbox=0,18,7,24 grip=8,24 muzzle=3.5,21.5 over=0,21,22,27
        PLAYF2F8 hash=571042098737180d front hands=21,25 seed=8,21;21,29;26,26 box=7,18,27,29 add=9,18,15,24 flash=3,21 fbox=0,19,6,24 grip=21,25 over=16,19,29,28
        PLAYF3F7 hash=8626b1ffdfe31697 front hands=21,24.5 seed=8,21;24,27 box=7,16,29,29 add=11,19,12,23;17,17,18,18 flash=3,20 fbox=0,18,6,23 grip=21,24.5 over=18,16,32,28
        PLAYF4F6 hash=de04fe7cf09f383a behind seed=6,19 box=6,18,9,21 flash=2,19 fbox=0,17,6,22 grip=9,20 tip=0,19.5
        PLAYF5 hash=78efae772f82d0b8 behind seed=18,22 box=15,19,22,26 grip=18.5,20 tip=18,18
        PLAYG1 hash=6c121286fb52c3ab front hands=7.5,24;29,20.5 seed=2,20;36,14 box=0,13,38,27 grip=7.5,24 over=4,22,11,27;24,16,38,23
        PLAYG2G8 hash=9b3bb0329af91276 front hands=11,21;4,25.5 seed=2,20 box=0,14,17,27 grip=5,26 muzzle=0.5,20.5 over=3,17,26,27
        PLAYG3G7 hash=43e699accace9906 front hands=19,23.5;5,21.5 seed=1,15;14,25 box=0,14,25,27 grip=19,23.5 tip=0,15 over=2,18,28,25
        PLAYG4G6 hash=c99102169f32ad84 behind seed=0,16;18,24 box=0,15,22,28 grip=10,19 tip=0,16
        PLAYG5 hash=286781e2696446d0 behind seed=31,20 box=28,17,33,28 grip=27,19
        """;
}
