import { memo } from "react";
import type { RequiredTopNavProps } from "../page-layout/page-layout";
import { useNavigate } from "react-router";
import { HamburgerMenu } from "../hamburger-menu/hamburger-menu";

export type TopNavigationProps = RequiredTopNavProps;

export const TopNavigation = memo(function TopNavigation(props: TopNavigationProps) {
  const { isHamburgerMenuOpen, onHamburgerMenuClick } = props;
  const navigate = useNavigate();

  return (
    <header className="flex h-16 items-center gap-3 px-4 md:px-6">
      <HamburgerMenu isOpen={isHamburgerMenuOpen} onClick={onHamburgerMenuClick} />
      <button
        className="group flex items-center gap-3 rounded-md px-2 py-1.5 hover:bg-white/5"
        onClick={() => navigate("/")}
      >
        <img className="h-8 w-7 transition-transform group-hover:scale-105" src="/logo.svg" alt="" />
        <span className="text-xl font-bold tracking-tight text-white">Nzb DAV</span>
      </button>
    </header>
  );
});