export interface AppNavigationMenuItem {
  routeName: string;
  titleKey: string;
  icon: string;
  permission?: string | null;
  order: number;
}
