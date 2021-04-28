Mercurial adapter for CCNet
===========================

[![Build status](https://ci.appveyor.com/api/projects/status/ppb21yojce9idat0/branch/master?svg=true)](https://ci.appveyor.com/project/dxrobot/hgadapter/branch/master)

Implements the [external source control](http://cruisecontrolnet.org/projects/ccnet/wiki/External) API.

Usage:

    GETMODS {date|(NOW)} prev_date --repo=path [--revset=revset] [--include=pattern] [--timeout=number]
    GETSOURCE target_path {max_date|(MAX)} --repo=path [--revset=revset] [--include=pattern] [--subdir=path]
    
See: [revsets](http://www.selenic.com/hg/help/revsets), [patterns](http://www.selenic.com/hg/help/patterns)

Samples:

    GETMODS (NOW) "2015-04-15 15:00:00" --repo=\path\to\repo --revset=branch(feature1) --include=listfile:files.txt
    GETSOURCE D:\temp (MAX) --repo=\path\to\repo --revset=branch(feature1) --include=listfile:files.txt
    
Direct usage in DX CCNet config:

    <cb:scope 
        hga_exec="\path\to\HgAdapter2.exe" 
        hga_extra="--repo=\path\to\repo --revset=branch(feature1) --include=pattern">
        <smart queue="Tests">
            <!-- . . . -->
            <sourcecontrol type="external" executable="$(hga_exec)" args="$(hga_extra)" />
            <tasks>
                <exec executable="cmd">
                    <buildArgs>
                        /c $(hga_exec) GETSOURCE "%CCNetWorkingDirectory%" "%DXCCNetBuildDateTime%" $(hga_extra)
                    </buildArgs>
                </exec>
            </tasks>
            <!-- . . . -->            
        </smart>
    </cb:scope>
