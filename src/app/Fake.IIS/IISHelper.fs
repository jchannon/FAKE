﻿[<AutoOpen>]
module Fake.IISHelper

open Microsoft.Web.Administration
open Fake.PermissionsHelper
open Fake.ProcessHelper

let private bindApplicationPool (appPool : ApplicationPool) (app : Application) =
    app.ApplicationPoolName <- appPool.Name

let Site (name : string) (protocol : string) (binding : string) (physicalPath : string) (appPool : string) (mgr : ServerManager) =
    let mutable site = mgr.Sites.[name] 
    match (site) with
    | null -> site <- mgr.Sites.Add(name, protocol, binding, physicalPath)
    | _ -> ()
    site.ApplicationDefaults.ApplicationPoolName <- appPool
    site

let ApplicationPool (name : string) (allow32on64:bool) (runtime:string) (mgr : ServerManager) = 
    let appPool = mgr.ApplicationPools.[name]
    match (appPool) with
    | null -> 
        let pool = mgr.ApplicationPools.Add(name)
        pool.Enable32BitAppOnWin64 <- allow32on64
        pool.ManagedRuntimeVersion <- runtime
        pool
    | _ ->
        appPool.Enable32BitAppOnWin64 <- allow32on64
        appPool.ManagedRuntimeVersion <- runtime
        appPool

let Application (virtualPath : string) (physicalPath : string) (site : Site) (mgr : ServerManager) =
    let app = site.Applications.[virtualPath]
    match (app) with
    | null -> site.Applications.Add(virtualPath, physicalPath)
    | _ -> app.VirtualDirectories.[0].PhysicalPath <- physicalPath; app

let commit (mgr : ServerManager) = mgr.CommitChanges()

let IIS (site : ServerManager -> Site) 
        (appPool : ServerManager -> ApplicationPool) 
        (app : (Site -> ServerManager -> Application) option) =
    use mgr = new ServerManager()
    requiresAdmin (fun _ -> 
        match app with
        | Some(app) -> bindApplicationPool (appPool mgr) (app (site mgr) mgr)
        | None -> bindApplicationPool (appPool mgr) (site mgr).Applications.[0]
        commit mgr
    )

let AppCmd (command : string) = 
    System.Console.WriteLine("Applying {0} via appcmd.exe", command)
    if 0 <> ExecProcess (fun info ->  
        info.FileName <- @"c:\windows\system32\inetsrv\appcmd.exe"
        info.Arguments <- command) (System.TimeSpan.FromSeconds(30.))
    then failwithf "AppCmd.exe %s failed." command
    ()

let UnlockSection (configPath : string) =
    requiresAdmin (fun _ -> AppCmd (sprintf "unlock config -section:%s" configPath))

let deleteSite (name : string) = 
    use mgr = new ServerManager()
    let site = mgr.Sites.[name]
    if site <> null then
        site.Delete()
        commit mgr 

let deleteApp (name : string) (site : Site) = 
    use mgr = new ServerManager()
    let app = site.Applications.[name]
    if app <> null then
        app.Delete()
        commit mgr

let deleteApplicationPool (name : string) = 
    use mgr = new ServerManager()
    let appPool = mgr.ApplicationPools.[name]
    if appPool <> null then
        appPool.Delete()
        commit mgr

open System.Diagnostics
open System
open System.IO
open System.Xml.Linq

let mutable IISExpressPath =
    let root = 
        if Environment.Is64BitOperatingSystem then
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        else
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)

    let filename = Path.Combine(root, "IIS Express", "iisexpress.exe")
    if File.Exists(filename) = false then
        failwithf "Could not find IIS Express at \"%s\". Please install IIS Express." filename
    
    filename

let xname s = XName.Get(s)

let createConfigFile(name, templateFileName, path, hostName, port:int) =
    let uniqueConfigFile = Path.Combine(Path.GetTempPath(), "iisexpress-" + Guid.NewGuid().ToString() + ".config")
   
    use template = File.OpenRead(templateFileName)
    let xml = XDocument.Load(template)   

    let sitesElement = xml.Root.Element(xname "system.applicationHost").Element(xname "sites")    
    let appElement =
        XElement(xname "site",
            XAttribute(xname "name", name),
            XAttribute(xname "id", "1"),
            XAttribute(xname "serverAutoStart", "true"),

            XElement(xname "application",
                XAttribute(xname "path", "/"),

                XElement(xname "virtualDirectory",
                    XAttribute(xname "path", "/"),
                    XAttribute(xname "physicalPath", path)
                )
            ),

            XElement(xname "bindings",
                XElement(xname "binding",
                    XAttribute(xname "protocol", "http"),
                    XAttribute(xname "bindingInformation", ":" + port.ToString() + ":" + hostName)
                )
            )
        )

    sitesElement.Add(appElement)

    xml.Save(uniqueConfigFile)
    uniqueConfigFile

let StartWebsite configFileName =
    ProcessStartInfo(
        FileName = IISExpressPath,
        Arguments = sprintf "/config:\"%s\" /siteid:%d" configFileName 1,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false)
    |> Process.Start

let p =
    createConfigFile("msu.Prestige", @"C:\Code\msu-perception\iisexpress-template.config", @"C:\Code\msu-perception\src\msu.Perception", "localhost", 54766)
    |> StartWebsite


p.Refresh()
p.Kill()